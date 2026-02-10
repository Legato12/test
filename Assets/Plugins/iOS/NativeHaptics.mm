#import <UIKit/UIKit.h>
#import <AudioToolbox/AudioToolbox.h>

// UIFeedbackGenerator API (iPhone 7+, iOS 10+)
extern "C" {

    void _NativeHaptics_ImpactLight() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            [gen prepare];
            [gen impactOccurred];
        }
    }

    void _NativeHaptics_ImpactMedium() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            [gen prepare];
            [gen impactOccurred];
        }
    }

    void _NativeHaptics_ImpactHeavy() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
            [gen prepare];
            [gen impactOccurred];
        }
    }

    // iOS 13+ — с интенсивностью (0.0 - 1.0)
    void _NativeHaptics_ImpactWithIntensity(int style, float intensity) {
        if (@available(iOS 13.0, *)) {
            UIImpactFeedbackStyle s = (UIImpactFeedbackStyle)style;
            UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
            [gen prepare];
            [gen impactOccurredWithIntensity:intensity];
        }
    }

    void _NativeHaptics_NotificationSuccess() {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackGenerator *gen = [[UINotificationFeedbackGenerator alloc] init];
            [gen prepare];
            [gen notificationOccurred:UINotificationFeedbackTypeSuccess];
        }
    }

    void _NativeHaptics_NotificationWarning() {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackGenerator *gen = [[UINotificationFeedbackGenerator alloc] init];
            [gen prepare];
            [gen notificationOccurred:UINotificationFeedbackTypeWarning];
        }
    }

    void _NativeHaptics_NotificationError() {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackGenerator *gen = [[UINotificationFeedbackGenerator alloc] init];
            [gen prepare];
            [gen notificationOccurred:UINotificationFeedbackTypeError];
        }
    }

    void _NativeHaptics_SelectionTick() {
        if (@available(iOS 10.0, *)) {
            UISelectionFeedbackGenerator *gen = [[UISelectionFeedbackGenerator alloc] init];
            [gen prepare];
            [gen selectionChanged];
        }
    }
}