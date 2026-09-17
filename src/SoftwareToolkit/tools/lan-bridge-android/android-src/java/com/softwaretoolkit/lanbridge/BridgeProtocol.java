package com.softwaretoolkit.lanbridge;

import android.app.NotificationManager;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.util.Log;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.NetworkInterface;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Collections;

final class BridgeProtocol {
    private static final String TAG = "LanBridge";

    private BridgeProtocol() { }

    static int notificationId(String requestId) {
        return 2000 + Math.abs(requestId.hashCode() % 100000);
    }

    static void handleDecision(final Context context, final String json, final boolean accepted, boolean openBrowser) {
        try {
            JSONObject request = new JSONObject(json);
            String requestId = request.optString("id", "");
            ((NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE)).cancel(notificationId(requestId));
            DeviceStore.removeRequest(context, requestId);
            new Thread(new Runnable() {
                @Override public void run() {
                    sendResponse(context.getApplicationContext(), json, accepted);
                }
            }, "bridge-response").start();
            if (accepted && openBrowser) { openOrigin(context, request); }
        } catch (Exception error) {
            Log.e(TAG, "Unable to process connection decision", error);
        }
    }

    private static void sendResponse(Context context, String json, boolean accepted) {
        HttpURLConnection connection = null;
        try {
            JSONObject request = new JSONObject(json);
            String originIp = request.optString("originIp", "");
            int originPort = request.optInt("originPort", 0);
            if (originIp.isEmpty() || originPort < 1 || originPort > 65535) {
                throw new IllegalArgumentException("Invalid origin address");
            }
            JSONObject response = new JSONObject(request.toString());
            response.put("status", accepted ? "accepted" : "rejected");
            response.put("responderId", DeviceStore.getId(context));
            response.put("responderName", DeviceStore.getName(context));
            response.put("responderIp", localIpv4());
            response.put("responderPort", BridgeService.HTTP_PORT);
            java.text.SimpleDateFormat timestamp = new java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", java.util.Locale.US);
            timestamp.setTimeZone(java.util.TimeZone.getTimeZone("UTC"));
            response.put("updated", timestamp.format(new java.util.Date()));
            Uri.Builder responseUrl = Uri.parse("http://" + originIp + ":" + originPort + "/api/connect/response/deliver").buildUpon();
            String originUrl = request.optString("originUrl", "");
            if (!originUrl.isEmpty()) {
                String token = Uri.parse(originUrl).getQueryParameter("t");
                if (token != null && !token.isEmpty()) { responseUrl.appendQueryParameter("t", token); }
            }
            postJson(responseUrl.build().toString(), response.toString());
        } catch (Exception error) {
            Log.e(TAG, "Unable to return connection response", error);
        } finally {
            if (connection != null) { connection.disconnect(); }
        }
    }

    private static void openOrigin(Context context, JSONObject request) throws JSONException {
        String originIp = request.optString("originIp", "");
        int originPort = request.optInt("originPort", 0);
        String originUrl = request.optString("originUrl", "");
        if (originUrl.isEmpty()) { originUrl = "http://" + originIp + ":" + originPort + "/"; }
        Uri parsed = Uri.parse(originUrl);
        if (!("http".equalsIgnoreCase(parsed.getScheme()) || "https".equalsIgnoreCase(parsed.getScheme())) || parsed.getHost() == null) {
            throw new IllegalArgumentException("Invalid origin URL");
        }
        Uri target = parsed.buildUpon()
                .appendQueryParameter("bridgeId", DeviceStore.getId(context))
                .appendQueryParameter("bridgeName", DeviceStore.getName(context))
                .appendQueryParameter("connectFrom", request.optString("from", ""))
                .appendQueryParameter("connectFromName", request.optString("fromName", ""))
                .build();
        Intent intent = new Intent(Intent.ACTION_VIEW, target);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        context.startActivity(intent);
    }

    private static void postJson(String address, String json) throws Exception {
        byte[] body = json.getBytes(StandardCharsets.UTF_8);
        HttpURLConnection connection = (HttpURLConnection) new URL(address).openConnection();
        try {
            connection.setConnectTimeout(5000);
            connection.setReadTimeout(5000);
            connection.setRequestMethod("POST");
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setFixedLengthStreamingMode(body.length);
            connection.setDoOutput(true);
            OutputStream output = connection.getOutputStream();
            output.write(body);
            output.close();
            int status = connection.getResponseCode();
            InputStream response = status >= 400 ? connection.getErrorStream() : connection.getInputStream();
            if (response != null) {
                ByteArrayOutputStream sink = new ByteArrayOutputStream();
                byte[] buffer = new byte[1024];
                int read;
                while ((read = response.read(buffer)) > 0 && sink.size() < 8192) { sink.write(buffer, 0, read); }
                response.close();
            }
            if (status < 200 || status >= 300) { throw new IllegalStateException("HTTP " + status); }
        } finally {
            connection.disconnect();
        }
    }

    static String localIpv4() {
        try {
            for (NetworkInterface network : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!network.isUp() || network.isLoopback()) { continue; }
                for (java.net.InetAddress address : Collections.list(network.getInetAddresses())) {
                    if (address instanceof Inet4Address && !address.isLoopbackAddress()) { return address.getHostAddress(); }
                }
            }
        } catch (Exception ignored) { }
        return "";
    }
}
