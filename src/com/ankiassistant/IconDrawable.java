package com.ankiassistant;

import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.drawable.Drawable;

/**
 * 用 Canvas 画的极简图标（24×24 虚拟网格）。
 * 不引入任何矢量资源/XML path，也就没有 API 版本兼容问题。
 */
public class IconDrawable extends Drawable {

    public static final int ADD = 0;      // 制卡：卡片 + 加号
    public static final int CARDS = 1;    // 浏览：两张叠放的卡片
    public static final int SLIDERS = 2;  // 设置：三根滑杆
    public static final int MENU = 3;     // 汉堡菜单

    private final int type;
    private final int color;
    private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);

    public IconDrawable(int type, int color) {
        this.type = type;
        this.color = color;
        p.setColor(color);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(1.9f);
        p.setStrokeCap(Paint.Cap.ROUND);
        p.setStrokeJoin(Paint.Join.ROUND);
    }

    @Override
    public void draw(Canvas canvas) {
        android.graphics.Rect b = getBounds();
        float w = b.width(), h = b.height();
        if (w <= 0 || h <= 0) return;
        canvas.save();
        canvas.translate(b.left, b.top);
        canvas.scale(w / 24f, h / 24f);
        p.setColor(color);

        switch (type) {
            case ADD: {
                RectF card = new RectF(2.5f, 5f, 21.5f, 19f);
                canvas.drawRoundRect(card, 3f, 3f, p);
                p.setStrokeWidth(1.7f);
                canvas.drawLine(12f, 9f, 12f, 15f, p);
                canvas.drawLine(9f, 12f, 15f, 12f, p);
                break;
            }
            case CARDS: {
                // 后面那张（只露上边）
                Path back = new Path();
                back.moveTo(7f, 3.5f);
                back.lineTo(20.5f, 3.5f);
                back.quadTo(21.5f, 3.5f, 21.5f, 4.5f);
                back.lineTo(21.5f, 12f);
                canvas.drawPath(back, p);
                // 前面那张
                RectF front = new RectF(2.5f, 8f, 17.5f, 20.5f);
                canvas.drawRoundRect(front, 2.6f, 2.6f, p);
                p.setStrokeWidth(1.5f);
                canvas.drawLine(5.5f, 12f, 14.5f, 12f, p);
                canvas.drawLine(5.5f, 15.5f, 11.5f, 15.5f, p);
                break;
            }
            case SLIDERS: {
                float[] ys = {6f, 12f, 18f};
                float[] kx = {8f, 16f, 11f};
                for (int i = 0; i < 3; i++) {
                    canvas.drawLine(3f, ys[i], 21f, ys[i], p);
                }
                for (int i = 0; i < 3; i++) {
                    p.setStyle(Paint.Style.FILL);
                    canvas.drawCircle(kx[i], ys[i], 3.1f, p);
                    // 中空：用背景色盖一层，再描边
                    p.setColor(0xFFFFFFFF);
                    canvas.drawCircle(kx[i], ys[i], 1.6f, p);
                    p.setColor(color);
                    p.setStyle(Paint.Style.STROKE);
                    canvas.drawCircle(kx[i], ys[i], 1.6f, p);
                }
                break;
            }
            case MENU: {
                canvas.drawLine(4f, 7f, 20f, 7f, p);
                canvas.drawLine(4f, 12f, 20f, 12f, p);
                canvas.drawLine(4f, 17f, 20f, 17f, p);
                break;
            }
        }
        canvas.restore();
    }

    @Override
    public void setAlpha(int alpha) { p.setAlpha(alpha); }

    @Override
    public void setColorFilter(android.graphics.ColorFilter cf) { p.setColorFilter(cf); }

    @Override
    public int getOpacity() { return android.graphics.PixelFormat.TRANSLUCENT; }
}
