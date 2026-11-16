# Testing

Tests use Unity Test Framework. Close the Editor before running batch tests against this project. Run commands from the repository root.

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults /tmp/shadow-bloom-edit.xml -logFile /tmp/shadow-bloom-edit.log
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults /tmp/shadow-bloom-play.xml -logFile /tmp/shadow-bloom-play.log
```

Adjust the executable path for your installation. Use `-testFilter ShadowBloom.Tests.<FixtureName>` for a specific fixture.

Inspect both the result XML and the log. An exit code alone is insufficient: this project has previously written results before a shutdown abort. Simulator tests are opt-in and require a running GUI XR session; batch mode cannot establish that session.

No new Unity runs were performed as part of the public-source packaging. Included tests cover geometry, matching, progression, saving, placement, UI and game flow. Tests and desktop simulation do not establish headset comfort, hand tracking quality, passthrough readability or device performance.

For a device check, exercise automatic and manual placement, the first tutorial, grabbing and regrabbing, hand-tracking loss, pause/resume, stage completion, the finale, sound, reduced motion and Assist. Check bright and dark tables and measure device performance separately.
