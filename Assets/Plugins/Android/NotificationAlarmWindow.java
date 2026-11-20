package com.clemtek.notifications;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;
import android.util.Log;

/**
 * Narrows the delivery window of a local notification scheduled by com.unity.mobile.notifications.
 *
 * The package schedules with AlarmManager.set(), an inexact alarm: Android delivers it anywhere from the fire time
 * to about 3/4 of the delay later (capped at 1 hour). setWindow() lets any app ask for a tighter window with no
 * permission (Android 12+ stretches windows under 10 minutes to 10 minutes). Exact alarms are not used: they need
 * a user-granted permission on Android 14+ and draw Play policy review.
 *
 * Battery Saver: plain alarms of background apps wait until it is turned off, so when it is on at scheduling time
 * the alarm becomes setAndAllowWhileIdle instead (fires under Battery Saver and Doze, no permission, OS-chosen time).
 *
 * How: an alarm is keyed by its PendingIntent, so setting a new alarm with an equal PendingIntent replaces the
 * package's alarm. The package builds it as getBroadcast(context, id, new Intent(context, UnityNotificationManager))
 * (see UnityNotificationManager.scheduleAlarmWithNotification / cancelPendingNotificationIntent); extras do not
 * take part in PendingIntent equality, so the same component + request code finds it.
 *
 * The package schedules on its own background thread, so this waits until that PendingIntent exists (and a beat
 * longer, so our call lands after the package's set()) before replacing the alarm. If the notification was
 * cancelled meanwhile, the PendingIntent is gone and nothing happens. If this never runs (process killed first,
 * or after a reboot reschedule), the package's inexact alarm still fires: the window is a best effort.
 *
 * Re-check after updating com.unity.mobile.notifications: the receiver class name and request code are its internals.
 */
public final class NotificationAlarmWindow {
    private static final String TAG = "NotificationWindow";
    private static final String RECEIVER = "com.unity.androidnotifications.UnityNotificationManager";
    private static final long START_DELAY_MS = 1000;
    private static final long SETTLE_MS = 300;   // after the PendingIntent appears, let the package's set() run first
    private static final long RETRY_MS = 200;
    private static final int MAX_TRIES = 25;      // ~5 s

    private NotificationAlarmWindow() {}

    /**
     * @param id           the notification id passed to SendNotificationWithExplicitID
     * @param fireTimeMs   wall-clock fire time, ms since the epoch (UTC)
     * @param windowMs     how late Android may deliver it
     */
    public static void apply(Context context, int id, long fireTimeMs, long windowMs) {
        if (context == null || windowMs <= 0) return;
        final Context app = context.getApplicationContext();
        // Start a moment later: a cancel-then-reschedule (every time the app is backgrounded) is queued on the
        // package's thread, and a PendingIntent found right away could still be the one about to be replaced.
        new Handler(Looper.getMainLooper()).postDelayed(
                () -> attempt(app, id, fireTimeMs, windowMs, 0, false), START_DELAY_MS);
    }

    private static void attempt(Context context, int id, long fireTimeMs, long windowMs, int tries, boolean settled) {
        Handler handler = new Handler(Looper.getMainLooper());
        PendingIntent broadcast = find(context, id);
        if (broadcast == null) {
            if (tries >= MAX_TRIES) {
                Log.w(TAG, "No scheduled alarm for notification " + id + "; keeping the default window");
                return;
            }
            handler.postDelayed(() -> attempt(context, id, fireTimeMs, windowMs, tries + 1, false), RETRY_MS);
            return;
        }
        if (!settled) {
            handler.postDelayed(() -> attempt(context, id, fireTimeMs, windowMs, tries, true), SETTLE_MS);
            return;
        }
        try {
            AlarmManager alarms = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
            PowerManager power = (PowerManager) context.getSystemService(Context.POWER_SERVICE);
            if (power != null && power.isPowerSaveMode()) {
                // Battery Saver holds plain alarms (set / setWindow) of background apps until it is turned off.
                // Allow-while-idle alarms still fire (Android picks the time, rate limited), so prefer arriving
                // late over not arriving. Saver switched on after this point still holds the windowed alarm.
                alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, fireTimeMs, broadcast);
                Log.d(TAG, "Notification " + id + ": power saving on, allow-while-idle alarm");
            } else {
                alarms.setWindow(AlarmManager.RTC_WAKEUP, fireTimeMs, windowMs, broadcast);
                Log.d(TAG, "Notification " + id + " window set to " + (windowMs / 60000) + " min");
            }
        } catch (Exception e) {
            Log.w(TAG, "Could not set the window for notification " + id + ": " + e.getMessage());
        }
    }

    private static PendingIntent find(Context context, int id) {
        Intent intent = new Intent();
        intent.setClassName(context, RECEIVER);
        return PendingIntent.getBroadcast(context, id, intent,
                PendingIntent.FLAG_NO_CREATE | PendingIntent.FLAG_IMMUTABLE);
    }
}
