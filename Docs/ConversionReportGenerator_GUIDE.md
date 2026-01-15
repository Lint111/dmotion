# Conversion Report Generator - User Guide

## Overview

The **Conversion Report Generator** automatically creates detailed HTML and Markdown reports after converting Unity AnimatorController to DMotion StateMachineAsset. These reports provide transparency about what was converted, what features were used, and any limitations encountered.

## Features

### What's Included in Reports

1. **Summary**: Overall conversion status (success/failure)
2. **Statistics**:
   - States converted
   - Transitions created
   - Parameters converted
   - Animation clips used
   - Blend trees converted
   - Sub-state machines created
   - Any State transitions

3. **Feature Comparison Table**:
   - Shows which Unity features were used
   - Maps Unity features to DMotion equivalents
   - Indicates conversion status (✓ Supported, ⚠ Partial, ✗ Not Supported)

4. **Warnings**: List of features that were converted with limitations or skipped

5. **Recommendations**: Actionable suggestions for improving the conversion or handling unsupported features

## How to Use

### Enabling Report Generation

Reports are **enabled by default**. To configure:

1. Open the Controller Bridge Config:
   - Menu: `DMotion > Open Controller Bridge Config`

2. Find the "Conversion Options" section

3. Toggle `Generate Conversion Report` (checked = enabled)

### Report Output

After conversion, two files are created in the same directory as the generated StateMachineAsset:

- `{ControllerName}_ConversionReport.html` - Rich HTML report with styling
- `{ControllerName}_ConversionReport.md` - Markdown report for version control

**Example**:
```
Assets/DMotion/Generated/
├── PlayerController_Generated.asset      (StateMachineAsset)
├── PlayerController_ConversionReport.html (HTML Report)
└── PlayerController_ConversionReport.md   (Markdown Report)
```

### Viewing Reports

**HTML Report**:
- Double-click the `.html` file to open in your web browser
- Styled with clean, professional formatting
- Color-coded status indicators
- Collapsible sections
- Mobile-friendly

**Markdown Report**:
- View in any Markdown viewer
- Great for including in documentation
- Can be committed to version control
- Plain text format

## Report Contents

### 1. Summary Section

Shows high-level conversion status:
```
✓ Success
```
or
```
✗ Failed
```

### 2. Statistics Table

| Metric | Count |
|--------|-------|
| States Converted | 15 |
| Transitions Created | 23 |
| Parameters Converted | 8 |
| Animation Clips | 12 |
| Blend Trees | 2 |
| Sub-State Machines | 1 |
| Any State Transitions | 3 |

### 3. Feature Comparison

| Feature | Unity | DMotion | Status |
|---------|-------|---------|--------|
| Single Clip States | Used | SingleClipStateAsset | ✓ Supported |
| 1D Blend Trees | Used | LinearBlendStateAsset | ✓ Supported |
| Sub-State Machines | Used | SubStateMachineStateAsset | ✓ Supported |
| Any State Transitions | Used | Native Support | ✓ Supported |
| Speed Parameter | Used | Not Supported | ✗ Not Supported |
| 2D Blend Trees | - | Not Supported | ✗ Not Supported |

**Status Indicators**:
- ✓ **Supported**: Full conversion with no limitations
- ⚠ **Partially Supported**: Converted but with some limitations
- ✗ **Not Supported**: Feature skipped or not implemented
- `-` **Not Used**: Feature not present in Unity controller

### 4. Warnings Section

Lists features that were converted with limitations:

- **Speed Parameter**: Speed parameters are not yet supported - speed will be constant
- **Trigger Parameters**: Converted to Bool - auto-reset must be implemented manually
- **Cycle Offset Ignored**: Some states have cycle offset which is not supported

### 5. Recommendations

Actionable suggestions based on conversion results:

**🔴 HIGH Priority**:
- Review warnings in Unity Console
- Critical issues that may affect runtime behavior

**🟡 MEDIUM Priority**:
- Trigger parameters need manual reset logic
- Speed parameters will be constant

**🟢 LOW Priority**:
- Minor limitations that rarely affect gameplay

## Use Cases

### 1. Development Workflow

Use reports to:
- Verify conversion accuracy
- Identify unsupported features early
- Plan workarounds for missing features
- Track conversion history in version control

### 2. Team Collaboration

Share reports with:
- Artists: Show which features are safe to use
- Designers: Communicate limitations
- Programmers: Identify areas needing custom systems

### 3. Documentation

Include Markdown reports in:
- Project documentation
- Technical design docs
- Migration guides
- Bug reports

### 4. Debugging

When animations don't work as expected:
1. Check the conversion report
2. Look for warnings about unsupported features
3. Review recommendations
4. Compare Unity controller with DMotion asset

## Feature Support Reference

### Fully Supported ✓

These Unity features convert with no limitations:

- **Single Clip States**: `SingleClipStateAsset`
- **1D Blend Trees**: `LinearBlendStateAsset`
- **Sub-State Machines**: `SubStateMachineStateAsset` (unlimited depth)
- **Any State Transitions**: Native global transitions
- **Bool Parameters**: `BoolParameterAsset`
- **Int Parameters**: `IntParameterAsset`
- **Float Parameters**: `FloatParameterAsset`
- **Exit Time Transitions**: Converted to absolute `EndTime`

### Partially Supported ⚠

These features work but with limitations:

- **Trigger Parameters**: Converted to `BoolParameterAsset` (auto-reset must be manual)
  - **Workaround**: Reset bool parameter after use in gameplay code

### Not Supported ✗

These features are skipped or not implemented:

- **Speed Parameter**: Animation speed from parameter
  - **Planned**: Phase 12.1 of Unity Controller Bridge roadmap
  - **Workaround**: Use constant speed, or implement custom speed control

- **2D Blend Trees**: Directional, Freeform, Cartesian
  - **Planned**: Phase 14.1 (high priority)
  - **Workaround**: Use 1D blend trees or manual state switching

- **Direct Blend Trees**: Manual blend weights
  - **Planned**: Phase 14.3
  - **Workaround**: Not available

- **Multiple Layers**: Only first layer converted
  - **Planned**: Phase 14.2
  - **Workaround**: Merge layers manually or use multiple state machines

- **Float Parameter Conditions**: Float comparisons in transitions
  - **Planned**: Phase 13
  - **Workaround**: Use Int or Bool parameters

- **Transition Offset**: Start time offset
  - **Limitation**: DMotion doesn't support this
  - **Workaround**: Adjust animation clips

- **Cycle Offset**: State start time offset
  - **Limitation**: Not supported
  - **Workaround**: Adjust animation clips

## Example Report

Here's what a typical conversion report looks like:

### HTML Report Preview

```
┌──────────────────────────────────────────┐
│ DMotion Conversion Report                │
│ PlayerController                         │
├──────────────────────────────────────────┤
│ Summary                                  │
│ Status: ✓ Success                        │
├──────────────────────────────────────────┤
│ Statistics                               │
│ States Converted: 12                     │
│ Transitions Created: 18                  │
│ Parameters Converted: 5                  │
│ Animation Clips: 10                      │
│ Blend Trees: 1                           │
│ Sub-State Machines: 1                    │
├──────────────────────────────────────────┤
│ Feature Comparison                       │
│ Single Clip States: ✓ Supported          │
│ 1D Blend Trees: ✓ Supported              │
│ Sub-State Machines: ✓ Supported          │
│ Any State: ✓ Supported                   │
├──────────────────────────────────────────┤
│ Recommendations                          │
│ 🟢 Conversion Completed Successfully     │
│ All features converted without issues    │
└──────────────────────────────────────────┘
```

## Configuration

### Enable/Disable Reports

**Via Inspector**:
1. `DMotion > Open Controller Bridge Config`
2. Toggle `Generate Conversion Report`

**Via Code**:
```csharp
var config = ControllerBridgeConfig.Instance;
config.GenerateConversionReport = false; // Disable
EditorUtility.SetDirty(config);
```

### Report Formats

Both formats are always generated when reports are enabled:
- HTML: Best for viewing/sharing
- Markdown: Best for version control/documentation

## Tips & Best Practices

### 1. Keep Reports for Historical Reference

Commit Markdown reports to version control to track:
- Feature usage over time
- Conversion improvements
- Regression detection

### 2. Share HTML Reports with Non-Programmers

HTML reports are easier to read for:
- Artists who need to know supported features
- Designers planning animator workflows
- QA testing animation issues

### 3. Use Reports to Plan Upgrades

Check reports to see which features you're using that aren't supported yet. Prioritize workarounds or wait for future DMotion updates.

### 4. Automate Report Review

In CI/CD pipelines:
```csharp
// Parse Markdown report
// Check for errors/warnings
// Fail build if critical issues found
```

## Troubleshooting

### Report Not Generated

**Problem**: No HTML/MD files created after conversion

**Solutions**:
1. Check `ControllerBridgeConfig.GenerateConversionReport` is enabled
2. Verify conversion succeeded (check console for errors)
3. Ensure output directory has write permissions
4. Manually refresh Asset Database (`Ctrl+R` / `Cmd+R`)

### Report Shows Wrong Features

**Problem**: Report says feature is used but it's not in controller

**Solutions**:
1. Refresh the conversion (delete generated asset and reconvert)
2. Check nested sub-state machines (features may be in nested states)
3. Verify you're looking at the correct Unity controller

### HTML Report Doesn't Open

**Problem**: Double-clicking HTML file doesn't open in browser

**Solutions**:
1. Right-click → Open With → Browser
2. Drag and drop onto browser window
3. Copy file outside Unity project folder

## Future Enhancements

Planned improvements to the report generator:

- **Interactive HTML**: Clickable state names to navigate Unity controller
- **Diff Reports**: Compare before/after conversions
- **Performance Metrics**: Estimated runtime memory/CPU cost
- **Custom Templates**: User-defined report formats
- **JSON Export**: Machine-readable format for automation

## FAQ

**Q: Do reports affect conversion performance?**
A: Minimal impact (<50ms). Report generation happens after conversion completes.

**Q: Can I customize report styling?**
A: Not yet. Custom templates are planned for a future release.

**Q: Are reports generated in builds?**
A: No. Reports are Editor-only and not included in builds.

**Q: Can I generate a report for an existing StateMachineAsset?**
A: Currently no. Reports are generated during conversion. Re-convert to get a report.

**Q: What if I want only Markdown or only HTML?**
A: Both are always generated when enabled. Ignore the format you don't need.

**Q: Can I parse reports programmatically?**
A: Yes! Markdown format is plain text and easy to parse. JSON export is planned.

## Support

**Issues**: GitHub Issues
**Docs**: `Docs/ConversionReportGenerator_*.md`
**Config**: `DMotion > Open Controller Bridge Config`

---

**Related Documentation**:
- `SubStateMachines_USER_GUIDE.md` - Sub-state machine usage
- `UnityControllerBridge_ActionPlan.md` - Feature roadmap
- `AnyState_Complete_Implementation_Summary.md` - Any State transitions
