package com.softwaretoolkit.lanbridge;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.Build;

public final class BootReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        if (!DeviceStore.isEnabled(context)) { return; }
        Intent service = new Intent(context, BridgeService.class);
        try {
            if (Build.VERSION.SDK_INT >= 26) { context.startForegroundService(service); }
            else { context.startService(service); }
        } catch (Exception ignored) { }
    }
}
