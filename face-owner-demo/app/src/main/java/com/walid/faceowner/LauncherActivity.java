package com.walid.faceowner;

import android.Manifest;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;

import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

public class LauncherActivity extends AppCompatActivity {
    private static final int CAMERA_REQUEST = 1001;
    private boolean permissionRequested = false;
    private boolean openedSettings = false;
    private boolean mainStarted = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        checkCameraPermission();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (openedSettings) {
            openedSettings = false;
            if (hasCameraPermission()) {
                startMain();
            } else {
                showPermissionDialog();
            }
        }
    }

    private boolean hasCameraPermission() {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA)
                == PackageManager.PERMISSION_GRANTED;
    }

    private void checkCameraPermission() {
        if (hasCameraPermission()) {
            startMain();
            return;
        }

        if (!permissionRequested) {
            permissionRequested = true;
            ActivityCompat.requestPermissions(
                    this,
                    new String[]{Manifest.permission.CAMERA},
                    CAMERA_REQUEST
            );
        }
    }

    @Override
    public void onRequestPermissionsResult(
            int requestCode,
            @NonNull String[] permissions,
            @NonNull int[] grantResults
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != CAMERA_REQUEST) return;

        if (grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
            startMain();
        } else {
            showPermissionDialog();
        }
    }

    private void showPermissionDialog() {
        if (isFinishing() || isDestroyed()) return;

        boolean canAskAgain = ActivityCompat.shouldShowRequestPermissionRationale(
                this,
                Manifest.permission.CAMERA
        );

        String message = canAskAgain
                ? "التطبيق يحتاج صلاحية الكاميرا لتسجيل الوجه والتحقق منه. لا يتم رفع الصور إلى الإنترنت."
                : "صلاحية الكاميرا مقفولة للتطبيق. افتح إعدادات التطبيق ثم فعّل Camera / الكاميرا، وبعدها ارجع للتطبيق.";

        AlertDialog.Builder builder = new AlertDialog.Builder(this)
                .setTitle("مطلوب إذن الكاميرا")
                .setMessage(message)
                .setCancelable(false);

        if (canAskAgain) {
            builder.setPositiveButton("السماح بالكاميرا", (dialog, which) -> {
                permissionRequested = false;
                checkCameraPermission();
            });
            builder.setNegativeButton("إغلاق", (dialog, which) -> finish());
        } else {
            builder.setPositiveButton("فتح إعدادات التطبيق", (dialog, which) -> openAppSettings());
            builder.setNegativeButton("إغلاق", (dialog, which) -> finish());
        }

        builder.show();
    }

    private void openAppSettings() {
        openedSettings = true;
        Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
        intent.setData(Uri.fromParts("package", getPackageName(), null));
        startActivity(intent);
    }

    private void startMain() {
        if (mainStarted || isFinishing()) return;
        mainStarted = true;
        Intent intent = new Intent(this, MainActivity.class);
        startActivity(intent);
        finish();
    }
}
