package com.softwaretoolkit.lanbridge;

import android.app.Activity;
import android.os.Bundle;

public final class ConnectionActionActivity extends Activity {
    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        String request = getIntent().getStringExtra("request");
        if (request != null) { BridgeProtocol.handleDecision(this, request, true, true); }
        finish();
    }
}
