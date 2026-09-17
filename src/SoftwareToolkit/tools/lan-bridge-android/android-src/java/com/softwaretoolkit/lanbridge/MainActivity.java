package com.softwaretoolkit.lanbridge;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

public final class MainActivity extends Activity {
    private final Handler handler = new Handler();
    private TextView status;
    private EditText deviceName;
    private Button startButton;
    private Button stopButton;
    private final Runnable refresh = new Runnable() {
        @Override public void run() {
            updateStatus();
            handler.postDelayed(this, 1000);
        }
    };

    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(buildContent());
        requestNotificationPermission();
        if (DeviceStore.isEnabled(this)) { startBridge(); }
    }

    @Override protected void onResume() {
        super.onResume();
        handler.post(refresh);
    }

    @Override protected void onPause() {
        handler.removeCallbacks(refresh);
        super.onPause();
    }

    private View buildContent() {
        int pad = dp(20);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(pad, pad, pad, pad);
        root.setBackgroundColor(Color.rgb(245, 247, 251));

        TextView title = text("蒲公英降落台", 26, Color.rgb(35, 31, 80));
        title.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(title);
        TextView subtitle = text("浏览器关闭时，也能收到局域网连接请求", 14, Color.DKGRAY);
        subtitle.setPadding(0, dp(6), 0, dp(20));
        root.addView(subtitle);

        status = text("", 16, Color.rgb(30, 64, 175));
        status.setTypeface(Typeface.DEFAULT_BOLD);
        status.setPadding(dp(14), dp(14), dp(14), dp(14));
        status.setBackgroundColor(Color.WHITE);
        root.addView(status, matchWrap());

        TextView id = text("设备 ID：" + DeviceStore.getId(this) + "\n监听端口：" + BridgeService.HTTP_PORT, 13, Color.GRAY);
        id.setPadding(0, dp(16), 0, dp(12));
        root.addView(id);

        TextView nameLabel = text("设备名称", 13, Color.DKGRAY);
        root.addView(nameLabel);
        deviceName = new EditText(this);
        deviceName.setText(DeviceStore.getName(this));
        deviceName.setSingleLine(true);
        deviceName.setTextSize(16);
        deviceName.setPadding(dp(12), dp(8), dp(12), dp(8));
        root.addView(deviceName, matchWrap());

        Button save = button("保存设备名称");
        save.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View view) {
                DeviceStore.setName(MainActivity.this, deviceName.getText().toString());
                Toast.makeText(MainActivity.this, "设备名称已保存", Toast.LENGTH_SHORT).show();
            }
        });
        root.addView(save, spacedButton());

        LinearLayout actions = new LinearLayout(this);
        actions.setOrientation(LinearLayout.HORIZONTAL);
        actions.setGravity(Gravity.CENTER);
        startButton = button("开启降落台");
        stopButton = button("停止降落台");
        startButton.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View view) { DeviceStore.setEnabled(MainActivity.this, true); startBridge(); } });
        stopButton.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View view) { DeviceStore.setEnabled(MainActivity.this, false); stopService(new Intent(MainActivity.this, BridgeService.class)); updateStatus(); } });
        LinearLayout.LayoutParams half = new LinearLayout.LayoutParams(0, dp(48), 1);
        half.setMargins(0, 0, dp(6), 0);
        actions.addView(startButton, half);
        LinearLayout.LayoutParams half2 = new LinearLayout.LayoutParams(0, dp(48), 1);
        half2.setMargins(dp(6), 0, 0, 0);
        actions.addView(stopButton, half2);
        root.addView(actions, spacedButton());

        TextView help = text("使用方法\n\n1. 保持手机和电脑连接同一 Wi-Fi。\n2. 开启蒲公英降落台并允许通知。\n3. 电脑上的局域网共享页面会自动发现此设备。\n4. 收到通知后点击“同意并打开”。\n\n为避免系统休眠中断监听，建议在电池设置中允许本应用后台运行。强行停止应用后，需要重新打开一次。", 14, Color.DKGRAY);
        help.setLineSpacing(0, 1.25f);
        help.setPadding(0, dp(22), 0, dp(20));
        root.addView(help);

        ScrollView scroll = new ScrollView(this);
        scroll.addView(root);
        return scroll;
    }

    private void startBridge() {
        Intent service = new Intent(this, BridgeService.class);
        if (Build.VERSION.SDK_INT >= 26) { startForegroundService(service); }
        else { startService(service); }
        updateStatus();
    }

    private void updateStatus() {
        if (status == null) { return; }
        boolean running = BridgeService.isRunning;
        status.setText(running ? "● 蒲公英降落台正在运行" : "○ 蒲公英降落台已停止");
        status.setTextColor(running ? Color.rgb(5, 150, 105) : Color.rgb(185, 28, 28));
        startButton.setEnabled(!running);
        stopButton.setEnabled(running);
    }

    private void requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 301);
        }
    }

    private TextView text(String value, int size, int color) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(size);
        view.setTextColor(color);
        return view;
    }

    private Button button(String value) {
        Button view = new Button(this);
        view.setText(value);
        view.setTextSize(14);
        view.setAllCaps(false);
        return view;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
    }

    private LinearLayout.LayoutParams spacedButton() {
        LinearLayout.LayoutParams params = matchWrap();
        params.setMargins(0, dp(12), 0, 0);
        return params;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
