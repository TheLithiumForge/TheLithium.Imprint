#import <XCTest/XCTest.h>

// The host communicates through this runner's simulator app container.
@interface ImprintForeground : XCTestCase
@end

@implementation ImprintForeground
- (void)testForeground {
    NSString *bundle = NSProcessInfo.processInfo.environment[@"IMPRINT_BUNDLE_ID"];
    XCTAssertNotNil(bundle);
    XCUIApplication *target = [[XCUIApplication alloc] initWithBundleIdentifier:bundle];
    NSString *directory = [NSTemporaryDirectory() stringByAppendingPathComponent:@"imprint"];
    NSFileManager *files = NSFileManager.defaultManager;
    XCTAssertTrue([files createDirectoryAtPath:directory withIntermediateDirectories:YES attributes:nil error:nil]);
    NSString *request = [directory stringByAppendingPathComponent:@"request"];
    NSString *stop = [directory stringByAppendingPathComponent:@"stop"];
    NSString *last = nil;
    [files createFileAtPath:[directory stringByAppendingPathComponent:@"ready"] contents:nil attributes:nil];
    while (![files fileExistsAtPath:stop]) {
        @autoreleasepool {
            NSString *query = [NSString stringWithContentsOfFile:request encoding:NSUTF8StringEncoding error:nil];
            if (query.length == 32 && ![query isEqualToString:last] &&
                [query rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdef"] invertedSet]].location == NSNotFound) {
                NSString *state = target.state == XCUIApplicationStateRunningForeground ? @"foreground" : @"background";
                [state writeToFile:[directory stringByAppendingPathComponent:query] atomically:YES encoding:NSUTF8StringEncoding error:nil];
                last = query;
            }
            [NSRunLoop.currentRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.02]];
        }
    }
}
@end
