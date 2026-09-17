package com.softwaretoolkit.lanbridge;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.IBinder;
import android.util.Log;

import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

public final class BridgeService extends Service {
    static final int HTTP_PORT = 8099;
    private static final int DISCOVERY_PORT = 8098;
    private static final String TAG = "LanBridge";
    private static final String CHANNEL_SERVICE = "bridge_service";
    private static final String CHANNEL_REQUESTS = "bridge_requests";
    private static final int FOREGROUND_ID = 1001;
    static volatile boolean isRunning;

    private final ExecutorService workers = Executors.newCachedThreadPool();
    private final ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
    private volatile boolean stopping;
    private ServerSocket serverSocket;

    @Override public void onCreate() {
        super.onCreate();
        createChannels();
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        startForeground(FOREGROUND_ID, serviceNotification("正在监听局域网连接请求"));
        if (!isRunning) {
            isRunning = true;
            stopping = false;
            DeviceStore.setEnabled(this, true);
            workers.execute(new Runnable() { @Override public void run() { listenHttp(); } });
            scheduler.scheduleAtFixedRate(new Runnable() { @Override public void run() { broadcastPresence(); } }, 0, 5, TimeUnit.SECONDS);
        }
        return START_STICKY;
    }

    @Override public void onDestroy() {
        stopping = true;
        isRunning = false;
        try { if (serverSocket != null) { serverSocket.close(); } } catch (Exception ignored) { }
        scheduler.shutdownNow();
        workers.shutdownNow();
        super.onDestroy();
    }

    @Override public IBinder onBind(Intent intent) { return null; }

    private void createChannels() {
        NotificationManager manager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        NotificationChannel service = new NotificationChannel(CHANNEL_SERVICE, "蒲公英降落台运行状态", NotificationManager.IMPORTANCE_LOW);
        service.setDescription("保证蒲公英降落台在后台持续运行");
        manager.createNotificationChannel(service);
        NotificationChannel requests = new NotificationChannel(CHANNEL_REQUESTS, "连接请求", NotificationManager.IMPORTANCE_HIGH);
        requests.setDescription("其他设备发来的局域网连接请求");
        requests.enableVibration(true);
        requests.enableLights(true);
        requests.setLightColor(Color.BLUE);
        manager.createNotificationChannel(requests);
    }

    private Notification serviceNotification(String text) {
        Intent open = new Intent(this, MainActivity.class);
        PendingIntent pending = PendingIntent.getActivity(this, 1, open, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this, CHANNEL_SERVICE)
                .setSmallIcon(com.softwaretoolkit.lanbridge.R.drawable.ic_bridge)
                .setContentTitle("蒲公英降落台已开启")
                .setContentText(text)
                .setContentIntent(pending)
                .setOngoing(true)
                .setCategory(Notification.CATEGORY_SERVICE)
                .build();
    }

    private void listenHttp() {
        try {
            serverSocket = new ServerSocket();
            serverSocket.setReuseAddress(true);
            serverSocket.bind(new InetSocketAddress(HTTP_PORT));
            while (!stopping) {
                final Socket socket = serverSocket.accept();
                socket.setSoTimeout(7000);
                workers.execute(new Runnable() { @Override public void run() { handleHttp(socket); } });
            }
        } catch (Exception error) {
            if (!stopping) {
                Log.e(TAG, "Bridge listener stopped", error);
                NotificationManager manager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
                manager.notify(FOREGROUND_ID, serviceNotification("监听失败：端口 " + HTTP_PORT + " 被占用"));
            }
        }
    }

    private void handleHttp(Socket socket) {
        try {
            InputStream input = new BufferedInputStream(socket.getInputStream());
            String requestLine = readLine(input);
            if (requestLine == null) { return; }
            String[] first = requestLine.split(" ");
            Map<String, String> headers = new HashMap<String, String>();
            String line;
            while ((line = readLine(input)) != null && !line.isEmpty()) {
                int colon = line.indexOf(':');
                if (colon > 0) { headers.put(line.substring(0, colon).trim().toLowerCase(Locale.US), line.substring(colon + 1).trim()); }
            }
            if (first.length < 2 || !"POST".equals(first[0]) || !"/api/connect/deliver".equals(first[1])) {
                writeResponse(socket, 404, "{\"error\":\"not found\"}");
                return;
            }
            int length = Integer.parseInt(headers.containsKey("content-length") ? headers.get("content-length") : "0");
            if (length < 2 || length > 65536) {
                writeResponse(socket, 400, "{\"error\":\"invalid body\"}");
                return;
            }
            byte[] body = new byte[length];
            int offset = 0;
            while (offset < length) {
                int read = input.read(body, offset, length - offset);
                if (read < 0) { throw new IllegalStateException("Unexpected end of request"); }
                offset += read;
            }
            JSONObject request = new JSONObject(new String(body, StandardCharsets.UTF_8));
            if (!DeviceStore.getId(this).equals(request.optString("to"))) {
                writeResponse(socket, 404, "{\"error\":\"device id mismatch\"}");
                return;
            }
            String requestId = request.optString("id", "");
            if (requestId.isEmpty()) {
                writeResponse(socket, 400, "{\"error\":\"missing id\"}");
                return;
            }
            String remoteIp = socket.getInetAddress().getHostAddress();
            int originPort = request.optInt("originPort", 0);
            if (originPort < 1 || originPort > 65535) {
                writeResponse(socket, 400, "{\"error\":\"invalid origin\"}");
                return;
            }
            Uri.Builder safeOrigin = Uri.parse("http://" + remoteIp + ":" + originPort + "/").buildUpon();
            String suppliedOrigin = request.optString("originUrl", "");
            if (!suppliedOrigin.isEmpty()) {
                String token = Uri.parse(suppliedOrigin).getQueryParameter("t");
                if (token != null && !token.isEmpty()) { safeOrigin.appendQueryParameter("t", token); }
            }
            request.put("originIp", remoteIp);
            request.put("originUrl", safeOrigin.build().toString());
            DeviceStore.saveRequest(this, requestId, request.toString());
            showConnectionRequest(request);
            writeResponse(socket, 200, "{\"ok\":true}");
        } catch (Exception error) {
            Log.e(TAG, "Invalid bridge HTTP request", error);
            try { writeResponse(socket, 400, "{\"error\":\"bad request\"}"); } catch (Exception ignored) { }
        } finally {
            try { socket.close(); } catch (Exception ignored) { }
        }
    }

    private void showConnectionRequest(JSONObject request) throws Exception {
        String id = request.getString("id");
        String json = request.toString();
        String fromName = request.optString("fromName", "附近设备");

        Intent accept = new Intent(this, ConnectionActionActivity.class)
                .putExtra("request", json)
                .setData(Uri.parse("lanbridge://request/" + id + "/accept"));
        PendingIntent acceptPending = PendingIntent.getActivity(this, BridgeProtocol.notificationId(id), accept,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Intent reject = new Intent(this, RejectReceiver.class)
                .putExtra("request", json)
                .setData(Uri.parse("lanbridge://request/" + id + "/reject"));
        PendingIntent rejectPending = PendingIntent.getBroadcast(this, BridgeProtocol.notificationId(id) + 1, reject,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        Notification.Action acceptAction = new Notification.Action.Builder(null, "同意并打开", acceptPending).build();
        Notification.Action rejectAction = new Notification.Action.Builder(null, "拒绝", rejectPending).build();
        Notification notification = new Notification.Builder(this, CHANNEL_REQUESTS)
                .setSmallIcon(com.softwaretoolkit.lanbridge.R.drawable.ic_bridge)
                .setContentTitle("收到局域网连接请求")
                .setContentText(fromName + " 希望与你连接")
                .setStyle(new Notification.BigTextStyle().bigText(fromName + " 希望与你连接。点击同意后将自动打开共享网页。"))
                .setContentIntent(acceptPending)
                .setAutoCancel(true)
                .setCategory(Notification.CATEGORY_CALL)
                .setPriority(Notification.PRIORITY_HIGH)
                .addAction(rejectAction)
                .addAction(acceptAction)
                .build();
        ((NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE)).notify(BridgeProtocol.notificationId(id), notification);
    }

    private void broadcastPresence() {
        DatagramSocket socket = null;
        try {
            JSONObject message = new JSONObject();
            message.put("id", DeviceStore.getId(this));
            message.put("name", DeviceStore.getName(this));
            message.put("port", HTTP_PORT);
            message.put("type", "bridge");
            message.put("capability", "connect-notification");
            byte[] bytes = message.toString().getBytes(StandardCharsets.UTF_8);
            socket = new DatagramSocket();
            socket.setBroadcast(true);
            sendPacket(socket, bytes, InetAddress.getByName("255.255.255.255"));
            for (NetworkInterface network : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!network.isUp() || network.isLoopback()) { continue; }
                for (InterfaceAddress address : network.getInterfaceAddresses()) {
                    if (address.getBroadcast() != null) { sendPacket(socket, bytes, address.getBroadcast()); }
                }
            }
        } catch (Exception error) {
            Log.w(TAG, "Unable to broadcast bridge presence", error);
        } finally {
            if (socket != null) { socket.close(); }
        }
    }

    private void sendPacket(DatagramSocket socket, byte[] bytes, InetAddress address) {
        try { socket.send(new DatagramPacket(bytes, bytes.length, address, DISCOVERY_PORT)); } catch (Exception ignored) { }
    }

    private static String readLine(InputStream input) throws Exception {
        ByteArrayOutputStream line = new ByteArrayOutputStream();
        int value;
        boolean sawCarriageReturn = false;
        while ((value = input.read()) >= 0) {
            if (value == '\n') { break; }
            if (sawCarriageReturn) { line.write('\r'); sawCarriageReturn = false; }
            if (value == '\r') { sawCarriageReturn = true; }
            else { line.write(value); }
            if (line.size() > 8192) { throw new IllegalArgumentException("Header too long"); }
        }
        if (value < 0 && line.size() == 0) { return null; }
        return line.toString("US-ASCII");
    }

    private static void writeResponse(Socket socket, int status, String json) throws Exception {
        byte[] body = json.getBytes(StandardCharsets.UTF_8);
        String reason = status == 200 ? "OK" : (status == 404 ? "Not Found" : "Bad Request");
        String headers = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.length + "\r\nConnection: close\r\n\r\n";
        OutputStream output = socket.getOutputStream();
        output.write(headers.getBytes(StandardCharsets.US_ASCII));
        output.write(body);
        output.flush();
    }
}
