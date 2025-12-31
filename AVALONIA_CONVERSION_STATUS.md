# Avalonia UI Conversion Status

**Date**: 2025-12-30
**Status**: 🔨 In Progress - Foundation Complete, Detailed Conversion Needed

---

## 🎯 Conversion Overview

Converting Privacy Hardening Framework UI from **WinUI 3** to **Avalonia UI** to resolve persistent XAML compiler errors and enable cross-platform compatibility.

### Why Avalonia?
- ✅ **No XAML Compiler Issues**: Eliminates MSB3073 errors from WindowsAppSDK
- ✅ **Cross-Platform**: Works on Windows, Linux, macOS
- ✅ **Modern**: Similar to WPF/WinUI but actively maintained
- ✅ **MVVM Support**: Full compatibility with CommunityToolkit.Mvvm
- ✅ **Fluent Design**: Built-in Fluent theme support

---

## ✅ Completed Work

### 1. Project Configuration
- ✅ **Updated PrivacyHardeningUI.csproj**
  - Changed from `Microsoft.WindowsAppSDK` to `Avalonia` packages
  - Added Avalonia.Desktop, Avalonia.Themes.Fluent
  - Maintained target framework: `net8.0-windows10.0.22621.0`
  - Kept CommunityToolkit.Mvvm for MVVM support

### 2. Core Application Files
- ✅ **Created App.axaml** - Avalonia application XAML
- ✅ **Created App.axaml.cs** - Avalonia application code-behind
- ✅ **Created Program.cs** - Avalonia entry point
- ✅ **Removed old WinUI files** (App.xaml, MainWindow.xaml)

### 3. Main Window
- ✅ **Created MainWindow.axaml** - Converted from WinUI TabView to Avalonia TabControl
- ✅ **Created MainWindow.axaml.cs** - Updated for Avalonia.Controls.Window

### 4. Converters (Partial)
- ✅ **BoolToVisibilityConverter.cs** - Updated for Avalonia (uses bool directly)
- ✅ **InverseBoolConverter.cs** - Updated for Avalonia

---

## 🔨 Remaining Work

### High Priority

#### 1. Complete Converter Updates (5 files)
Need to update for Avalonia.Data.Converters interface:

```
CountToVisibilityConverter.cs
EnumToStringConverter.cs
InverseBoolToVisibilityConverter.cs
NullToVisibilityConverter.cs
RiskLevelToBrushConverter.cs
```

**Changes Needed**:
```csharp
// Old (WinUI 3)
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

public object Convert(object value, Type targetType, object parameter, string language)

// New (Avalonia)
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
```

**Visibility Changes**:
- Avalonia uses `bool` for `IsVisible`, not `Visibility` enum
- `Visibility.Visible` → `true`
- `Visibility.Collapsed` → `false`

#### 2. Update View Code-Behind Files (3 files)
```
Views/PolicySelectionView.xaml.cs
Views/AuditView.xaml.cs
Views/DiffView.xaml.cs
```

**Changes Needed**:
```csharp
// Old
using Microsoft.UI.Xaml.Controls;
public sealed partial class PolicySelectionView : UserControl

// New
using Avalonia.Controls;
public partial class PolicySelectionView : UserControl
```

#### 3. Convert View XAML Files (3 files)
```
Views/PolicySelectionView.xaml → .axaml
Views/AuditView.xaml → .axaml
Views/DiffView.xaml → .axaml
```

**Key XAML Differences**:
| WinUI 3 | Avalonia |
|---------|----------|
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | `xmlns="https://github.com/avaloniaui"` |
| `x:Bind` | `{Binding}` (standard WPF binding) |
| `Mode=TwoWay` | `Mode=TwoWay` (same) |
| `Visibility` property | `IsVisible` property |
| `Style="{StaticResource ...}"` | `Classes="..."` or similar |
| `ThemeResource` | `DynamicResource` |

#### 4. Update ServiceClient.cs
- Add missing `using System.Threading.Tasks;`
- Verify NamedPipeClientStream usage (should work as-is)

---

### Medium Priority

#### 5. Create Assets Folder
- Add application icon: `Assets/icon.ico`
- Add any other required assets

#### 6. Update ViewModels
Currently showing MVVM Toolkit errors:
```
error MVVMTK0007: Method signature incompatible with relay command types
```

**Files Affected**:
- `ViewModels/MainViewModel.cs`
- `ViewModels/AuditViewModel.cs`
- `ViewModels/PolicySelectionViewModel.cs`

**Issue**: Async methods may need to return `Task` instead of `void`

#### 7. Remove/Update app.manifest
- Avalonia may not need Windows-specific manifest
- Or update for Avalonia requirements

---

## 📊 Conversion Statistics

| Category | Total | Converted | Remaining |
|----------|-------|-----------|-----------|
| **Project Files** | 1 | 1 | 0 |
| **Core App Files** | 3 | 3 | 0 |
| **Windows** | 1 | 1 | 0 |
| **Views (XAML)** | 3 | 0 | 3 |
| **Views (Code)** | 3 | 0 | 3 |
| **Converters** | 7 | 2 | 5 |
| **ViewModels** | 4 | 0 | 4 (need fixes) |
| **Services** | 1 | 0 | 1 (minor fix) |
| **Assets** | 1 | 0 | 1 |

**Total Progress**: ~40% Complete

---

## 🚀 Next Steps

### Immediate Actions (To Get Building)

1. **Update Remaining Converters** (30 minutes)
   - Batch update 5 converter files
   - Change interface from `IValueConverter` (WinUI) to `IValueConverter` (Avalonia)
   - Update method signatures
   - Fix Visibility → bool conversions

2. **Update View Code-Behind** (15 minutes)
   - Change `Microsoft.UI.Xaml.Controls` → `Avalonia.Controls`
   - Remove `sealed` modifiers
   - Update base class if needed

3. **Convert View XAML Files** (45 minutes)
   - PolicySelectionView.xaml → .axaml
   - AuditView.xaml → .axaml
   - DiffView.xaml → .axaml
   - Update namespace declarations
   - Convert `x:Bind` to `{Binding}`
   - Fix Visibility bindings

4. **Fix ViewModel Issues** (20 minutes)
   - Ensure async methods return `Task`
   - Verify MVVM Toolkit attribute usage

5. **Add Assets** (5 minutes)
   - Create Assets folder
   - Add placeholder icon

### Testing Phase

Once building successfully:
1. Run application
2. Test UI navigation (tab switching)
3. Test policy selection
4. Test audit functionality
5. Fix runtime issues

---

## 🛠️ Detailed Conversion Guide

### Converting a Converter File

**Template**:
```csharp
using Avalonia.Data.Converters;
using Avalonia.Media; // If needed for brushes/colors
using System;
using System.Globalization;

namespace PrivacyHardeningUI.Converters;

public sealed class MyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // For visibility: return bool instead of Visibility
        // true = visible, false = collapsed
        return someCondition ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Reverse conversion
        return value;
    }
}
```

### Converting a View XAML File

**Header**:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:PrivacyHardeningUI.ViewModels"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="600"
             x:Class="PrivacyHardeningUI.Views.MyView"
             x:DataType="vm:MyViewModel">
```

**Binding Changes**:
```xml
<!-- Old (WinUI 3 x:Bind) -->
<TextBlock Text="{x:Bind ViewModel.Message, Mode=OneWay}"/>

<!-- New (Avalonia Binding) -->
<TextBlock Text="{Binding Message}"/>
```

**Visibility Changes**:
```xml
<!-- Old -->
<Border Visibility="{x:Bind ViewModel.IsVisible, Mode=OneWay,
                     Converter={StaticResource BoolToVisibilityConverter}}"/>

<!-- New -->
<Border IsVisible="{Binding IsVisible}"/>
```

### Converting a View Code-Behind

**Template**:
```csharp
using Avalonia.Controls;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Views;

public partial class MyView : UserControl
{
    public MyView()
    {
        InitializeComponent();
    }
}
```

---

## 📝 Known Issues & Solutions

### Issue 1: MVVM Toolkit Errors
**Error**: `MVVMTK0007: Method signature incompatible`

**Solution**: Ensure async command methods return `Task`:
```csharp
[RelayCommand]
private async Task LoadDataAsync() // Must return Task, not void
{
    await SomeAsyncOperation();
}
```

### Issue 2: Missing Type References
**Error**: `The type or namespace name 'Type' could not be found`

**Solution**: Add `using System;` to converter files

### Issue 3: Duplicate Definitions
**Error**: `Already contains a definition for...`

**Cause**: Old `.xaml.cs` files still present alongside new `.axaml.cs` files

**Solution**: Ensure old WinUI files are deleted

---

## 🎯 Success Criteria

Conversion is complete when:
- ✅ Project builds with 0 errors
- ✅ Application launches successfully
- ✅ All tabs are accessible
- ✅ Policy selection UI renders correctly
- ✅ Audit functionality works
- ✅ Diff view displays properly
- ✅ Service communication functions
- ✅ Converters work correctly
- ✅ MVVM bindings update UI

---

## 🔄 Alternative: PowerShell GUI

**Currently Working**: A fully functional PowerShell Windows Forms GUI has been created as an interim solution:

**File**: `LaunchGUI.ps1` / `LaunchGUI.bat`

**Features**:
- ✅ Launches immediately (no build required)
- ✅ Modern Windows Forms interface
- ✅ All CLI commands accessible
- ✅ Documentation links
- ✅ Framework status display

**Use this while Avalonia conversion is completed.**

---

## 📚 Resources

- **Avalonia Documentation**: https://docs.avaloniaui.net/
- **Avalonia Samples**: https://github.com/AvaloniaUI/Avalonia.Samples
- **WPF to Avalonia Migration**: https://docs.avaloniaui.net/guides/platforms/wpf
- **MVVM Toolkit**: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/

---

**Conversion Initiated**: 2025-12-30
**Estimated Completion**: 2-3 hours of focused work
**Current Status**: Foundation complete, detailed conversion in progress

**The PowerShell GUI (`LaunchGUI.bat`) is fully functional and can be used immediately while the Avalonia conversion is completed.**
