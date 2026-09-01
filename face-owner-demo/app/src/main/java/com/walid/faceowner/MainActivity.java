package com.walid.faceowner;

import android.Manifest;
import android.app.AlertDialog;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PointF;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.util.Base64;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.camera.core.CameraSelector;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.ImageCapture;
import androidx.camera.core.ImageProxy;
import androidx.camera.core.Preview;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.camera.view.PreviewView;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;
import androidx.exifinterface.media.ExifInterface;

import com.google.common.util.concurrent.ListenableFuture;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.face.Face;
import com.google.mlkit.vision.face.FaceDetector;
import com.google.mlkit.vision.face.FaceDetectorOptions;
import com.google.mlkit.vision.face.FaceLandmark;
import com.google.mlkit.vision.face.FaceDetection;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

public class MainActivity extends AppCompatActivity {
    private static final int CAMERA_REQUEST = 41;
    private static final String PREFS = "owner_face_prefs";
    private static final String KEY_DESCRIPTOR = "descriptor";
    private static final float MATCH_THRESHOLD = 0.68f;

    private PreviewView previewView;
    private FaceOverlay overlay;
    private TextView titleText;
    private TextView instructionText;
    private TextView modeText;
    private ImageCapture imageCapture;
    private ImageAnalysis imageAnalysis;
    private ProcessCameraProvider cameraProvider;
    private FaceDetector faceDetector;
    private final ExecutorService cameraExecutor = Executors.newSingleThreadExecutor();
    private final AtomicBoolean analyzing = new AtomicBoolean(false);

    private boolean enrollmentMode;
    private boolean livenessPassed = false;
    private int challengeState = 0;
    private int stableOpenFrames = 0;
    private int centeredFrames = 0;
    private int missingFaceFrames = 0;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().setStatusBarColor(Color.BLACK);
        getWindow().setNavigationBarColor(Color.BLACK);

        enrollmentMode = !getSharedPreferences(PREFS, MODE_PRIVATE).contains(KEY_DESCRIPTOR);
        setupDetector();
        buildUi();

        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
            startCamera();
        } else {
            ActivityCompat.requestPermissions(this, new String[]{Manifest.permission.CAMERA}, CAMERA_REQUEST);
        }
    }

    private void setupDetector() {
        FaceDetectorOptions options = new FaceDetectorOptions.Builder()
                .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                .setLandmarkMode(FaceDetectorOptions.LANDMARK_MODE_ALL)
                .setClassificationMode(FaceDetectorOptions.CLASSIFICATION_MODE_ALL)
                .enableTracking()
                .build();
        faceDetector = FaceDetection.getClient(options);
    }

    private void buildUi() {
        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.rgb(5, 14, 30));

        previewView = new PreviewView(this);
        previewView.setScaleType(PreviewView.ScaleType.FILL_CENTER);
        root.addView(previewView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        overlay = new FaceOverlay(this);
        root.addView(overlay, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        LinearLayout topPanel = new LinearLayout(this);
        topPanel.setOrientation(LinearLayout.VERTICAL);
        topPanel.setGravity(Gravity.CENTER_HORIZONTAL);
        topPanel.setPadding(dp(20), dp(22), dp(20), dp(18));
        GradientDrawable panelBg = new GradientDrawable();
        panelBg.setColor(0xD9101B31);
        panelBg.setCornerRadius(dp(24));
        panelBg.setStroke(dp(1), 0x55C9A75D);
        topPanel.setBackground(panelBg);

        modeText = new TextView(this);
        modeText.setText(enrollmentMode ? "التسجيل الأول" : "التحقق من صاحب الهاتف");
        modeText.setTextColor(0xFFC9A75D);
        modeText.setTextSize(14);
        modeText.setGravity(Gravity.CENTER);

        titleText = new TextView(this);
        titleText.setText(enrollmentMode ? "سجّل وجهك لأول مرة" : "تأكيد الهوية");
        titleText.setTextColor(Color.WHITE);
        titleText.setTextSize(25);
        titleText.setGravity(Gravity.CENTER);
        titleText.setPadding(0, dp(8), 0, dp(6));

        instructionText = new TextView(this);
        instructionText.setText("انظر إلى الكاميرا بشكل مباشر");
        instructionText.setTextColor(0xFFE8E8E8);
        instructionText.setTextSize(17);
        instructionText.setGravity(Gravity.CENTER);

        topPanel.addView(modeText, matchWrap());
        topPanel.addView(titleText, matchWrap());
        topPanel.addView(instructionText, matchWrap());

        FrameLayout.LayoutParams topParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        topParams.gravity = Gravity.TOP;
        topParams.setMargins(dp(18), dp(62), dp(18), 0);
        root.addView(topPanel, topParams);

        TextView privacy = new TextView(this);
        privacy.setText("● التحقق يعمل على هذا الجهاز فقط   •   الصورة والبيانات محفوظة محلياً");
        privacy.setTextColor(0xFFD7C59C);
        privacy.setTextSize(12);
        privacy.setGravity(Gravity.CENTER);
        privacy.setPadding(dp(12), dp(10), dp(12), dp(10));
        GradientDrawable privacyBg = new GradientDrawable();
        privacyBg.setColor(0xB8111A2A);
        privacyBg.setCornerRadius(dp(18));
        privacy.setBackground(privacyBg);

        FrameLayout.LayoutParams privacyParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        privacyParams.gravity = Gravity.BOTTOM;
        privacyParams.setMargins(dp(20), 0, dp(20), dp(34));
        root.addView(privacy, privacyParams);

        setContentView(root);
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void startCamera() {
        ListenableFuture<ProcessCameraProvider> future = ProcessCameraProvider.getInstance(this);
        future.addListener(() -> {
            try {
                cameraProvider = future.get();
                bindUseCases();
            } catch (Exception e) {
                showFatal("تعذر تشغيل الكاميرا", e.getMessage());
            }
        }, ContextCompat.getMainExecutor(this));
    }

    private void bindUseCases() {
        cameraProvider.unbindAll();

        Preview preview = new Preview.Builder().build();
        preview.setSurfaceProvider(previewView.getSurfaceProvider());

        imageCapture = new ImageCapture.Builder()
                .setCaptureMode(ImageCapture.CAPTURE_MODE_MINIMIZE_LATENCY)
                .build();

        imageAnalysis = new ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build();
        imageAnalysis.setAnalyzer(cameraExecutor, this::analyzeFrame);

        CameraSelector front = new CameraSelector.Builder()
                .requireLensFacing(CameraSelector.LENS_FACING_FRONT)
                .build();

        cameraProvider.bindToLifecycle(this, front, preview, imageCapture, imageAnalysis);
    }

    private void analyzeFrame(@NonNull ImageProxy imageProxy) {
        if (livenessPassed || !analyzing.compareAndSet(false, true)) {
            imageProxy.close();
            return;
        }
        if (imageProxy.getImage() == null) {
            analyzing.set(false);
            imageProxy.close();
            return;
        }

        InputImage image = InputImage.fromMediaImage(
                imageProxy.getImage(), imageProxy.getImageInfo().getRotationDegrees());

        faceDetector.process(image)
                .addOnSuccessListener(this::handleLiveness)
                .addOnFailureListener(e -> updateInstruction("حاول إبقاء وجهك داخل الإطار", 0xFFFFC857))
                .addOnCompleteListener(task -> {
                    analyzing.set(false);
                    imageProxy.close();
                });
    }

    private void handleLiveness(List<Face> faces) {
        if (faces.size() != 1) {
            missingFaceFrames++;
            if (missingFaceFrames > 4) {
                updateInstruction(faces.isEmpty() ? "ضع وجهك داخل الإطار" : "يجب ظهور وجه واحد فقط", 0xFFFFC857);
                overlay.setState(FaceOverlay.STATE_NEUTRAL);
            }
            return;
        }
        missingFaceFrames = 0;
        Face face = faces.get(0);
        Float left = face.getLeftEyeOpenProbability();
        Float right = face.getRightEyeOpenProbability();
        float eyeOpen = (left != null && right != null) ? (left + right) / 2f : -1f;
        float yaw = face.getHeadEulerAngleY();

        if (challengeState == 0) {
            updateInstruction("ثبّت وجهك وانظر إلى الكاميرا", 0xFFFFFFFF);
            overlay.setState(FaceOverlay.STATE_NEUTRAL);
            if (eyeOpen > 0.65f && Math.abs(yaw) < 12f) stableOpenFrames++; else stableOpenFrames = 0;
            if (stableOpenFrames >= 4) {
                challengeState = 1;
                updateInstruction("ارمش الآن", 0xFFC9A75D);
            }
        } else if (challengeState == 1) {
            if (eyeOpen >= 0f && eyeOpen < 0.35f) {
                challengeState = 2;
                updateInstruction("لف وجهك قليلاً إلى أحد الجانبين", 0xFFC9A75D);
            }
        } else if (challengeState == 2) {
            if (Math.abs(yaw) > 16f) {
                challengeState = 3;
                centeredFrames = 0;
                updateInstruction("ممتاز — ارجع للمنتصف وثبّت وجهك", 0xFF7DDC9A);
                overlay.setState(FaceOverlay.STATE_GOOD);
            }
        } else if (challengeState == 3) {
            if (Math.abs(yaw) < 7f && eyeOpen > 0.45f) centeredFrames++; else centeredFrames = 0;
            if (centeredFrames >= 5) {
                livenessPassed = true;
                overlay.setState(FaceOverlay.STATE_GOOD);
                updateInstruction("تم إثبات الحيوية — جارٍ فحص الوجه…", 0xFF7DDC9A);
                if (imageAnalysis != null) imageAnalysis.clearAnalyzer();
                previewView.postDelayed(this::captureForRecognition, 350);
            }
        }
    }

    private void updateInstruction(String text, int color) {
        runOnUiThread(() -> {
            instructionText.setText(text);
            instructionText.setTextColor(color);
        });
    }

    private void captureForRecognition() {
        if (imageCapture == null) return;
        File candidate = new File(getCacheDir(), "candidate_face.jpg");
        ImageCapture.OutputFileOptions options = new ImageCapture.OutputFileOptions.Builder(candidate).build();
        imageCapture.takePicture(options, ContextCompat.getMainExecutor(this), new ImageCapture.OnImageSavedCallback() {
            @Override
            public void onImageSaved(@NonNull ImageCapture.OutputFileResults outputFileResults) {
                processCaptured(candidate);
            }

            @Override
            public void onError(@NonNull ImageCaptureException exception) {
                showRetry("تعذر التقاط الصورة", "حاول مرة أخرى.");
            }
        });
    }

    private void processCaptured(File file) {
        Bitmap bitmap = loadOrientedBitmap(file);
        if (bitmap == null) {
            showRetry("خطأ في الصورة", "تعذر قراءة الصورة الملتقطة.");
            return;
        }

        faceDetector.process(InputImage.fromBitmap(bitmap, 0))
                .addOnSuccessListener(faces -> {
                    if (faces.size() != 1) {
                        showRetry("تعذر التحقق", "يجب أن يظهر وجه واحد بوضوح.");
                        return;
                    }
                    Face face = largestFace(faces);
                    float[] descriptor = buildDescriptor(bitmap, face);
                    if (descriptor == null) {
                        showRetry("تعذر قراءة الوجه", "اقترب قليلاً من الكاميرا وحاول مرة أخرى.");
                        return;
                    }
                    if (enrollmentMode) {
                        saveEnrollment(file, descriptor);
                    } else {
                        verifyCandidate(descriptor);
                    }
                })
                .addOnFailureListener(e -> showRetry("تعذر تحليل الوجه", "حاول مرة أخرى في إضاءة أفضل."));
    }

    private Face largestFace(List<Face> faces) {
        Face best = faces.get(0);
        int bestArea = best.getBoundingBox().width() * best.getBoundingBox().height();
        for (Face f : faces) {
            int area = f.getBoundingBox().width() * f.getBoundingBox().height();
            if (area > bestArea) {
                best = f;
                bestArea = area;
            }
        }
        return best;
    }

    private void saveEnrollment(File source, float[] descriptor) {
        try {
            File dest = new File(getFilesDir(), "owner_reference.jpg");
            try (FileInputStream in = new FileInputStream(source); FileOutputStream out = new FileOutputStream(dest)) {
                byte[] buffer = new byte[8192];
                int n;
                while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
            }
            getSharedPreferences(PREFS, MODE_PRIVATE).edit()
                    .putString(KEY_DESCRIPTOR, encodeDescriptor(descriptor))
                    .apply();

            new AlertDialog.Builder(this)
                    .setTitle("تم تسجيل صاحب الهاتف")
                    .setMessage("تم حفظ الصورة وبيانات الوجه محلياً على هذا الجهاز فقط.\n\nفي المرة القادمة سيطلب التطبيق إثبات الحيوية ثم يتأكد أن الوجه هو نفس الشخص.")
                    .setPositiveButton("تم", null)
                    .setCancelable(false)
                    .show();
        } catch (Exception e) {
            showRetry("تعذر حفظ التسجيل", e.getMessage());
        }
    }

    private void verifyCandidate(float[] candidate) {
        String encoded = getSharedPreferences(PREFS, MODE_PRIVATE).getString(KEY_DESCRIPTOR, null);
        float[] reference = decodeDescriptor(encoded);
        if (reference == null || reference.length != candidate.length) {
            showFatal("بيانات التسجيل غير صالحة", "امسح بيانات التطبيق وسجّل الوجه من جديد.");
            return;
        }

        float similarity = cosine(reference, candidate);
        if (similarity >= MATCH_THRESHOLD) {
            overlay.setState(FaceOverlay.STATE_GOOD);
            new AlertDialog.Builder(this)
                    .setTitle("تم التأكد")
                    .setMessage("مرحباً صاحب الهاتف")
                    .setPositiveButton("موافق", null)
                    .setCancelable(false)
                    .show();
        } else {
            overlay.setState(FaceOverlay.STATE_BAD);
            new AlertDialog.Builder(this)
                    .setTitle("تعذر التحقق")
                    .setMessage("تم اجتياز اختبار الحيوية، لكن الوجه لا يطابق صاحب الهاتف المسجل.\n\nحاول مرة أخرى في نفس مستوى الإضاءة وبدون نظارة أو غطاء للوجه.")
                    .setPositiveButton("إعادة المحاولة", (d, w) -> resetChallenge())
                    .setCancelable(false)
                    .show();
        }
    }

    private void resetChallenge() {
        livenessPassed = false;
        challengeState = 0;
        stableOpenFrames = 0;
        centeredFrames = 0;
        missingFaceFrames = 0;
        overlay.setState(FaceOverlay.STATE_NEUTRAL);
        updateInstruction("انظر إلى الكاميرا بشكل مباشر", Color.WHITE);
        if (imageAnalysis != null) imageAnalysis.setAnalyzer(cameraExecutor, this::analyzeFrame);
    }

    private void showRetry(String title, String message) {
        runOnUiThread(() -> new AlertDialog.Builder(this)
                .setTitle(title)
                .setMessage(message)
                .setPositiveButton("إعادة المحاولة", (d, w) -> resetChallenge())
                .setCancelable(false)
                .show());
    }

    private void showFatal(String title, String message) {
        runOnUiThread(() -> new AlertDialog.Builder(this)
                .setTitle(title)
                .setMessage(message == null ? "حدث خطأ غير متوقع." : message)
                .setPositiveButton("إغلاق", (d, w) -> finish())
                .setCancelable(false)
                .show());
    }

    private Bitmap loadOrientedBitmap(File file) {
        try {
            Bitmap bitmap = BitmapFactory.decodeFile(file.getAbsolutePath());
            if (bitmap == null) return null;
            ExifInterface exif = new ExifInterface(file.getAbsolutePath());
            int orientation = exif.getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL);
            Matrix m = new Matrix();
            if (orientation == ExifInterface.ORIENTATION_ROTATE_90) m.postRotate(90);
            else if (orientation == ExifInterface.ORIENTATION_ROTATE_180) m.postRotate(180);
            else if (orientation == ExifInterface.ORIENTATION_ROTATE_270) m.postRotate(270);
            else if (orientation == ExifInterface.ORIENTATION_FLIP_HORIZONTAL) m.preScale(-1, 1);
            if (!m.isIdentity()) {
                bitmap = Bitmap.createBitmap(bitmap, 0, 0, bitmap.getWidth(), bitmap.getHeight(), m, true);
            }
            return bitmap;
        } catch (Exception e) {
            return null;
        }
    }

    private float[] buildDescriptor(Bitmap source, Face face) {
        try {
            Bitmap aligned = alignFace(source, face, 64);
            if (aligned == null) return null;
            final int outSize = 32;
            float[] v = new float[outSize * outSize];
            int idx = 0;
            for (int y = 0; y < outSize; y++) {
                for (int x = 0; x < outSize; x++) {
                    float sum = 0;
                    for (int dy = 0; dy < 2; dy++) {
                        for (int dx = 0; dx < 2; dx++) {
                            int c = aligned.getPixel(x * 2 + dx, y * 2 + dy);
                            float gray = 0.299f * Color.red(c) + 0.587f * Color.green(c) + 0.114f * Color.blue(c);
                            sum += gray;
                        }
                    }
                    v[idx++] = sum / 4f;
                }
            }

            float mean = 0;
            for (float f : v) mean += f;
            mean /= v.length;
            float norm = 0;
            for (int i = 0; i < v.length; i++) {
                v[i] -= mean;
                norm += v[i] * v[i];
            }
            norm = (float) Math.sqrt(norm) + 1e-6f;
            for (int i = 0; i < v.length; i++) v[i] /= norm;
            return v;
        } catch (Exception e) {
            return null;
        }
    }

    private Bitmap alignFace(Bitmap source, Face face, int size) {
        FaceLandmark le = face.getLandmark(FaceLandmark.LEFT_EYE);
        FaceLandmark re = face.getLandmark(FaceLandmark.RIGHT_EYE);
        FaceLandmark nose = face.getLandmark(FaceLandmark.NOSE_BASE);
        if (le != null && re != null && nose != null) {
            PointF lp = le.getPosition();
            PointF rp = re.getPosition();
            PointF np = nose.getPosition();
            float[] src = {lp.x, lp.y, rp.x, rp.y, np.x, np.y};
            float[] dst = {18f, 22f, 46f, 22f, 32f, 36f};
            Matrix matrix = new Matrix();
            if (matrix.setPolyToPoly(src, 0, dst, 0, 3)) {
                Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
                Canvas canvas = new Canvas(out);
                canvas.drawColor(Color.BLACK);
                Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
                canvas.drawBitmap(source, matrix, paint);
                return out;
            }
        }

        Rect b = face.getBoundingBox();
        int marginX = Math.round(b.width() * 0.22f);
        int marginTop = Math.round(b.height() * 0.32f);
        int marginBottom = Math.round(b.height() * 0.18f);
        int left = Math.max(0, b.left - marginX);
        int top = Math.max(0, b.top - marginTop);
        int right = Math.min(source.getWidth(), b.right + marginX);
        int bottom = Math.min(source.getHeight(), b.bottom + marginBottom);
        if (right - left < 40 || bottom - top < 40) return null;
        Bitmap crop = Bitmap.createBitmap(source, left, top, right - left, bottom - top);
        return Bitmap.createScaledBitmap(crop, size, size, true);
    }

    private String encodeDescriptor(float[] v) {
        ByteBuffer buffer = ByteBuffer.allocate(v.length * 4).order(ByteOrder.LITTLE_ENDIAN);
        for (float f : v) buffer.putFloat(f);
        return Base64.encodeToString(buffer.array(), Base64.NO_WRAP);
    }

    private float[] decodeDescriptor(String s) {
        if (s == null) return null;
        try {
            byte[] bytes = Base64.decode(s, Base64.NO_WRAP);
            if (bytes.length % 4 != 0) return null;
            ByteBuffer buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
            float[] v = new float[bytes.length / 4];
            for (int i = 0; i < v.length; i++) v[i] = buffer.getFloat();
            return v;
        } catch (Exception e) {
            return null;
        }
    }

    private float cosine(float[] a, float[] b) {
        float dot = 0, aa = 0, bb = 0;
        for (int i = 0; i < a.length; i++) {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }
        return dot / ((float) Math.sqrt(aa * bb) + 1e-6f);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions, @NonNull int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == CAMERA_REQUEST && grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
            startCamera();
        } else if (requestCode == CAMERA_REQUEST) {
            showFatal("صلاحية الكاميرا مطلوبة", "بدون الكاميرا لا يمكن تسجيل الوجه أو التحقق منه.");
        }
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        cameraExecutor.shutdown();
        if (faceDetector != null) faceDetector.close();
    }

    public static class FaceOverlay extends View {
        static final int STATE_NEUTRAL = 0;
        static final int STATE_GOOD = 1;
        static final int STATE_BAD = 2;
        private final Paint dimPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint ringPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private int state = STATE_NEUTRAL;

        public FaceOverlay(android.content.Context context) {
            super(context);
            setLayerType(View.LAYER_TYPE_SOFTWARE, null);
            dimPaint.setColor(0x66000000);
            ringPaint.setStyle(Paint.Style.STROKE);
            ringPaint.setStrokeWidth(6f * getResources().getDisplayMetrics().density);
        }

        void setState(int state) {
            this.state = state;
            invalidate();
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            float w = getWidth();
            float h = getHeight();
            float ovalW = w * 0.78f;
            float ovalH = Math.min(h * 0.56f, ovalW * 1.35f);
            float cx = w / 2f;
            float cy = h * 0.53f;
            RectF oval = new RectF(cx - ovalW / 2f, cy - ovalH / 2f, cx + ovalW / 2f, cy + ovalH / 2f);

            Path path = new Path();
            path.setFillType(Path.FillType.EVEN_ODD);
            path.addRect(0, 0, w, h, Path.Direction.CW);
            path.addOval(oval, Path.Direction.CCW);
            canvas.drawPath(path, dimPaint);

            int color = state == STATE_GOOD ? 0xFF54D982 : state == STATE_BAD ? 0xFFFF4D4F : 0xFFE5C36A;
            ringPaint.setColor(color);
            ringPaint.setShadowLayer(18f, 0, 0, color);
            canvas.drawOval(oval, ringPaint);
        }
    }
}
