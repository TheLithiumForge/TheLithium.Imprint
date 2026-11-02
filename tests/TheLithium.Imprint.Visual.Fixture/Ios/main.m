// The iOS fixture: the left half red and the right half in the color of the "--right r,g,b" launch argument,
// blue by default. The capture driver tests look for both halves.
#import <UIKit/UIKit.h>

@interface FixtureView : UIView
@property(nonatomic, strong) UIColor *right;
@end

@implementation FixtureView
- (void)drawRect:(CGRect)rect {
    CGFloat middle = self.bounds.size.width / 2;
    [[UIColor colorWithRed:220 / 255.0 green:30 / 255.0 blue:30 / 255.0 alpha:1] setFill];
    UIRectFill(CGRectMake(0, 0, middle, self.bounds.size.height));
    [self.right setFill];
    UIRectFill(CGRectMake(middle, 0, self.bounds.size.width - middle, self.bounds.size.height));
}
@end

@interface FixtureDelegate : UIResponder <UIApplicationDelegate>
@property(nonatomic, strong) UIWindow *window;
@end

@implementation FixtureDelegate
- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)options {
    UIColor *right = [UIColor colorWithRed:30 / 255.0 green:60 / 255.0 blue:220 / 255.0 alpha:1];
    NSArray<NSString *> *arguments = NSProcessInfo.processInfo.arguments;
    NSUInteger index = [arguments indexOfObject:@"--right"];
    if (index != NSNotFound && index + 1 < arguments.count) {
        NSArray<NSString *> *parts = [arguments[index + 1] componentsSeparatedByString:@","];
        right = [UIColor colorWithRed:parts[0].doubleValue / 255.0 green:parts[1].doubleValue / 255.0 blue:parts[2].doubleValue / 255.0 alpha:1];
    }

    FixtureView *view = [[FixtureView alloc] init];
    view.right = right;
    UIViewController *controller = [[UIViewController alloc] init];
    controller.view = view;
    self.window = [[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
    self.window.rootViewController = controller;
    [self.window makeKeyAndVisible];
    return YES;
}
@end

int main(int argc, char *argv[]) {
    @autoreleasepool {
        return UIApplicationMain(argc, argv, nil, NSStringFromClass(FixtureDelegate.class));
    }
}
