package dev.thelithium.imprint.fixture;

import android.app.Activity;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.os.Bundle;
import android.view.View;

/**
 * Draws the left half red and the right half in the color of the "right" extra, blue by default. The capture driver
 * tests look for both halves, so a wrong app in front or lost extras fail.
 */
public final class FixtureActivity extends Activity {
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        int right = Color.rgb(30, 60, 220);
        String extra = getIntent().getStringExtra("right");
        if (extra != null) {
            String[] parts = extra.split(",");
            right = Color.rgb(Integer.parseInt(parts[0]), Integer.parseInt(parts[1]), Integer.parseInt(parts[2]));
        }
        setContentView(new Halves(this, right));
    }

    private static final class Halves extends View {
        private final Paint left = new Paint();
        private final Paint right = new Paint();

        Halves(Context context, int rightColor) {
            super(context);
            left.setColor(Color.rgb(220, 30, 30));
            right.setColor(rightColor);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            float middle = getWidth() / 2f;
            canvas.drawRect(0, 0, middle, getHeight(), left);
            canvas.drawRect(middle, 0, getWidth(), getHeight(), right);
        }
    }
}
