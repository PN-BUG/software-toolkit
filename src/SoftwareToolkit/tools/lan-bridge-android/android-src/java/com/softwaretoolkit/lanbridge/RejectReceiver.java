package com.softwaretoolkit.lanbridge;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

public final class RejectReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        String request = intent.getStringExtra("request");
        if (request != null) { BridgeProtocol.handleDecision(context, request, false, false); }
    }
}
