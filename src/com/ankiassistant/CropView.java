package com.ankiassistant;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.MotionEvent;
import android.view.ScaleGestureDetector;
import android.view.View;

/**
 * 方形裁剪框：图片可以拖动、双指缩放，框内就是最终头像。
 *
 * 只有"正方形 + 拖动 + 缩放"这三件事，够用且好懂：
 *   · 手指拖动 → 移动图片
 *   · 双指捏合 → 缩放（最小铺满裁剪框）
 *   · 确定时按当前可见区域映射回原图，输出指定边长（默认 256）的方形图
 */
public class CropView extends View {

    private final Bitmap src;
    private final Matrix matrix = new Matrix();
    private final Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
    private final Paint dim = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint frame = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final ScaleGestureDetector scaleDetector;

    private float scale = 1f, baseScale = 1f, dx = 0f, dy = 0f;
    private float lastX, lastY;
    private int side;                 // 裁剪框边长（像素）

    public CropView(Context c, Bitmap bitmap) {
        super(c);
        this.src = bitmap;
        dim.setColor(0x99000000);
        frame.setColor(0xFFFFFFFF);
        frame.setStyle(Paint.Style.STROKE);
        frame.setStrokeWidth(Ui.dp(2));
        scaleDetector = new ScaleGestureDetector(c,
                new ScaleGestureDetector.SimpleOnScaleGestureListener() {
            @Override public boolean onScale(ScaleGestureDetector d) {
                float next = scale * d.getScaleFactor();
                scale = Math.max(baseScale, Math.min(baseScale * 6f, next));
                clamp();
                invalidate();
                return true;
            }
        });
    }

    @Override protected void onSizeChanged(int w, int h, int ow, int oh) {
        super.onSizeChanged(w, h, ow, oh);
        side = Math.min(w, h);
        baseScale = Math.max((float) side / src.getWidth(), (float) side / src.getHeight());
        scale = baseScale;
        dx = (side - src.getWidth() * scale) / 2f + (w - side) / 2f;
        dy = (side - src.getHeight() * scale) / 2f + (h - side) / 2f;
        clamp();
    }

    /** 保证裁剪框内始终有图（不许拖出空白） */
    private void clamp() {
        float w = src.getWidth() * scale, h = src.getHeight() * scale;
        float left = (getWidth() - side) / 2f, top = (getHeight() - side) / 2f;
        if (w <= side) dx = left + (side - w) / 2f;
        else dx = Math.min(left, Math.max(left + side - w, dx));
        if (h <= side) dy = top + (side - h) / 2f;
        else dy = Math.min(top, Math.max(top + side - h, dy));
    }

    @Override protected void onDraw(Canvas cv) {
        int w = getWidth(), h = getHeight();
        int left = (w - side) / 2, top = (h - side) / 2;
        matrix.reset();
        matrix.postScale(scale, scale);
        matrix.postTranslate(dx, dy);
        cv.save();
        cv.clipRect(left, top, left + side, top + side);
        cv.drawBitmap(src, matrix, paint);
        cv.restore();
        // 框外压暗 + 白色方框
        cv.drawRect(0, 0, w, top, dim);
        cv.drawRect(0, top + side, w, h, dim);
        cv.drawRect(0, top, left, top + side, dim);
        cv.drawRect(left + side, top, w, top + side, dim);
        cv.drawRect(left, top, left + side, top + side, frame);
    }

    @Override public boolean onTouchEvent(MotionEvent e) {
        scaleDetector.onTouchEvent(e);
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                lastX = e.getX(); lastY = e.getY();
                getParent().requestDisallowInterceptTouchEvent(true);
                return true;
            case MotionEvent.ACTION_MOVE:
                if (!scaleDetector.isInProgress()) {
                    dx += e.getX() - lastX;
                    dy += e.getY() - lastY;
                    lastX = e.getX(); lastY = e.getY();
                    clamp();
                    invalidate();
                }
                return true;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL:
                getParent().requestDisallowInterceptTouchEvent(false);
                return true;
            default:
                return true;
        }
    }

    /** 按当前可见区域生成头像（边长 size；调用方负责回收） */
    public Bitmap cropped(int size) {
        int w = getWidth(), h = getHeight();
        int left = (w - side) / 2, top = (h - side) / 2;
        Bitmap out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas cv = new Canvas(out);
        Matrix m = new Matrix();
        float k = (float) size / side;
        m.postScale(k, k);
        m.postTranslate(-left * k, -top * k);
        m.postScale(scale, scale);
        m.postTranslate(dx, dy);
        Paint p = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
        cv.drawBitmap(src, m, p);
        return out;
    }
}
