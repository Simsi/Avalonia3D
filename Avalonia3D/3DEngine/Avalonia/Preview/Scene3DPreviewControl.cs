using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Avalonia.Interaction;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Preview;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;
using ProjectionHelper3D = ThreeDEngine.Core.Math.ProjectionHelper;

namespace ThreeDEngine.Avalonia.Preview;

public sealed partial class Scene3DPreviewControl : UserControl
{
    private readonly Scene3DControl _viewport;
    private readonly ListBox _partList;
    private readonly TextBox _objectFilterBox;
    private readonly ComboBox _previewSelector;
    private readonly TextBlock _sceneSummaryText;
    private readonly TextBlock _selectionHeaderText;
    private readonly TextBlock _selectionDetailsText;
    private readonly TextBlock _statusText;
    private readonly Button _refreshButton;
    private readonly Button _applyButton;
    private readonly Button _copySnippetButton;
    private readonly Button _resetButton;
    private readonly CheckBox _autoApplyCheckBox;
    private readonly TextBox _nameBox;
    private readonly CheckBox _visibleCheckBox;
    private readonly CheckBox _pickableCheckBox;
    private readonly CheckBox _manipulationCheckBox;
    private readonly TextBox _positionXBox;
    private readonly TextBox _positionYBox;
    private readonly TextBox _positionZBox;
    private readonly TextBox _rotationXBox;
    private readonly TextBox _rotationYBox;
    private readonly TextBox _rotationZBox;
    private readonly TextBox _scaleXBox;
    private readonly TextBox _scaleYBox;
    private readonly TextBox _scaleZBox;
    private readonly TextBox _colorRBox;
    private readonly TextBox _colorGBox;
    private readonly TextBox _colorBBox;
    private readonly TextBox _colorABox;
    private readonly TextBox _opacityBox;
    private readonly ComboBox _lightingBox;
    private readonly ComboBox _surfaceBox;
    private readonly ComboBox _cullBox;
    private readonly Border _highScalePanel;
    private readonly TextBox _lodDetailedBox;
    private readonly TextBox _lodSimplifiedBox;
    private readonly TextBox _lodProxyBox;
    private readonly TextBox _lodDrawBox;
    private readonly TextBox _lodFadeBox;
    private readonly CheckBox _lodBillboardCheckBox;
    private readonly TextBox _snippetBox;
    private readonly ComboBox _createTypeBox;
    private readonly TextBox _createNameBox;
    private readonly TextBox _createSizeXBox;
    private readonly TextBox _createSizeYBox;
    private readonly TextBox _createSizeZBox;
    private readonly TextBox _createColorRBox;
    private readonly TextBox _createColorGBox;
    private readonly TextBox _createColorBBox;
    private readonly TextBox _createColorABox;
    private readonly Button _createObjectButton;
    private readonly Button _duplicateObjectButton;
    private readonly Button _deleteObjectButton;
    private readonly Button _clearWorkbenchButton;
    private readonly CheckBox _ghostUnselectedCheckBox;
    private readonly CheckBox _hideUnselectedCheckBox;
    private readonly CheckBox _showOnlyWorkbenchCheckBox;
    private readonly CheckBox _keepSelectionPinnedCheckBox;
    private readonly Button _soloSelectedButton;
    private readonly Button _resetDebugViewButton;
    private readonly Button _copySceneSnippetButton;
    private readonly Button _copyDiagnosticsButton;
    private readonly Button _exportSourceButton;
    private readonly TextBox _sourceTargetBox;
    private readonly CheckBox _showBasisCheckBox;
    private readonly CheckBox _showGroundGridCheckBox;
    private readonly CheckBox _showDebugOverlayCheckBox;
    private readonly CheckBox _showBoundsCheckBox;
    private readonly CheckBox _showCollidersCheckBox;
    private readonly CheckBox _showPickingRayCheckBox;
    private readonly CheckBox _enablePhysicsCheckBox;
    private readonly TextBox _cameraNearBox;
    private readonly TextBox _cameraFarBox;
    private readonly TextBox _drawDistanceBox;
    private readonly TextBox _distanceFadeBox;
    private readonly CheckBox _enableDistanceFadeCheckBox;
    private readonly CheckBox _enableHighScaleLodCheckBox;
    private readonly CheckBox _adaptivePerformanceCheckBox;
    private readonly CheckBox _directionalLightEnabledCheckBox;
    private readonly TextBox _directionalLightIntensityBox;
    private readonly TextBox _directionalLightXBox;
    private readonly TextBox _directionalLightYBox;
    private readonly TextBox _directionalLightZBox;
    private readonly CheckBox _pointLightEnabledCheckBox;
    private readonly TextBox _pointLightIntensityBox;
    private readonly TextBox _pointLightRangeBox;
    private readonly TextBox _pointLightXBox;
    private readonly TextBox _pointLightYBox;
    private readonly TextBox _pointLightZBox;
    private readonly Button _applySceneSettingsButton;
    private readonly CheckBox _rigidbodyCheckBox;
    private readonly CheckBox _rigidbodyKinematicCheckBox;
    private readonly CheckBox _rigidbodyGravityCheckBox;
    private readonly TextBox _rigidbodyMassBox;
    private readonly TextBox _rigidbodyFrictionBox;
    private readonly TextBox _rigidbodyRestitutionBox;
    private readonly TextBlock _spaceReadoutText;
    private readonly Border _primitivePanel;
    private readonly TextBlock _primitiveTypeText;
    private readonly TextBox _primitiveABox;
    private readonly TextBox _primitiveBBox;
    private readonly TextBox _primitiveCBox;
    private readonly TextBox _primitiveSegmentsABox;
    private readonly TextBox _primitiveSegmentsBBox;
    private readonly ComboBox _eventTypeBox;
    private readonly TextBox _eventCodeBox;
    private readonly TextBox _eventHintsBox;
    private readonly Button _copyEventSnippetButton;
    private readonly CheckBox _showLightGizmosCheckBox;
    private readonly Button _toggleLeftPanelButton;
    private readonly Button _toggleRightPanelButton;
    private readonly Border _leftPane;
    private readonly Border _rightPane;
    private readonly ColumnDefinition _leftColumn;
    private readonly ColumnDefinition _leftSplitterColumn;
    private readonly ColumnDefinition _rightSplitterColumn;
    private readonly ColumnDefinition _rightColumn;
    private readonly DebuggerPhysicsService _debuggerPhysics;
    private readonly DebuggerSelectionService _selectionService;

    private IReadOnlyList<PreviewScene3D> _previews = Array.Empty<PreviewScene3D>();
    private List<PreviewObjectEntry> _listedObjects = new();
    private Object3D? _selectedObject;
    private bool _updatingPreviewSelector;
    private bool _updatingInspector;
    private bool _debugVisualsApplying;
    private readonly Dictionary<string, DebugVisualState> _debugVisualStates = new(StringComparer.Ordinal);
    private string? _sourceAssemblyPath;
    private string? _sourceTypeFullName;
    private string? _sourceProjectPath;
    private string? _sourcePatchDirectory;
    private SourceTargetInfo? _sourceTargetInfo;
    private Func<DebuggerSourceExportRequest, Task<DebuggerSourceExportResult>>? _sourceExportHandler;
    private readonly List<Object3D> _spaceGuideObjects = new();
    private readonly Dictionary<string, DebugEventBinding> _eventBindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _runtimeEventHandlersAttached = new(StringComparer.Ordinal);
    private string _eventHintsText = "Hints are generated from engine sources when a preview is loaded.";
    private bool _spaceGuidesUpdating;
    private bool _updatingSceneSettings;

    public Scene3DPreviewControl()
    {
        _viewport = new Scene3DControl
        {
            ShowPerformanceMetrics = true,
            ShowCenterCursor = false
        };

        _debuggerPhysics = new DebuggerPhysicsService(
            () => _viewport.Scene,
            () => _enablePhysicsCheckBox.IsChecked == true,
            () => TopLevel.GetTopLevel(this) is not null,
            () => _selectedObject,
            RefreshSelectionAfterPhysics);
        _selectionService = new DebuggerSelectionService();

        _partList = new ListBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _partList.SelectionChanged += OnPartSelectionChanged;
        _partList.DoubleTapped += (_, _) => FocusSelectedObject();

        _objectFilterBox = new TextBox
        {
            Watermark = "Filter by name/type/id",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _objectFilterBox.TextChanged += (_, _) => RebuildObjectListPreservingSelection();

        _previewSelector = new ComboBox
        {
            MinWidth = 160d,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _previewSelector.SelectionChanged += OnPreviewSelectionChanged;

        _refreshButton = new Button { Content = "Refresh" };
        _refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        var frameButton = new Button { Content = "Frame" };
        frameButton.Click += (_, _) => FocusSelectedObject();

        _sceneSummaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12d,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 214, 222))
        };

        _selectionHeaderText = new TextBlock
        {
            Text = "No selection",
            FontWeight = FontWeight.SemiBold,
            FontSize = 15d,
            TextWrapping = TextWrapping.Wrap
        };

        _selectionDetailsText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 12d
        };

        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12d,
            Foreground = new SolidColorBrush(Color.FromRgb(178, 204, 255))
        };

        _autoApplyCheckBox = new CheckBox
        {
            Content = "Auto apply valid values",
            IsChecked = true
        };

        _nameBox = CreateEditorBox("name");
        _visibleCheckBox = CreateEditorCheckBox("Visible");
        _pickableCheckBox = CreateEditorCheckBox("Pickable");
        _manipulationCheckBox = CreateEditorCheckBox("Manipulatable");

        _positionXBox = CreateEditorBox("x");
        _positionYBox = CreateEditorBox("y");
        _positionZBox = CreateEditorBox("z");
        _rotationXBox = CreateEditorBox("x°");
        _rotationYBox = CreateEditorBox("y°");
        _rotationZBox = CreateEditorBox("z°");
        _scaleXBox = CreateEditorBox("x");
        _scaleYBox = CreateEditorBox("y");
        _scaleZBox = CreateEditorBox("z");
        _colorRBox = CreateEditorBox("r");
        _colorGBox = CreateEditorBox("g");
        _colorBBox = CreateEditorBox("b");
        _colorABox = CreateEditorBox("a");
        _opacityBox = CreateEditorBox("0..1");

        _lightingBox = CreateEnumBox<LightingMode>();
        _surfaceBox = CreateEnumBox<SurfaceMode>();
        _cullBox = CreateEnumBox<CullMode>();

        _lodDetailedBox = CreateEditorBox("detailed");
        _lodSimplifiedBox = CreateEditorBox("simplified");
        _lodProxyBox = CreateEditorBox("proxy");
        _lodDrawBox = CreateEditorBox("draw");
        _lodFadeBox = CreateEditorBox("fade");
        _lodBillboardCheckBox = CreateEditorCheckBox("Billboard fallback");

        _applyButton = new Button { Content = "Apply" };
        _applyButton.Click += (_, _) => TryApplyInspectorValues(sender: null, rebuildList: true, forceStatus: true);

        _resetButton = new Button { Content = "Reload selected" };
        _resetButton.Click += (_, _) => LoadInspectorFromSelection();

        _copySnippetButton = new Button { Content = "Copy code" };
        _copySnippetButton.Click += async (_, _) =>
        {
            var text = _snippetBox.Text ?? string.Empty;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && !string.IsNullOrWhiteSpace(text))
            {
                await clipboard.SetTextAsync(text);
                SetStatus("Snippet copied to clipboard.", isError: false);
            }
        };

        _snippetBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 12d,
            MinHeight = 112d,
            IsReadOnly = true
        };

        _createTypeBox = new ComboBox
        {
            ItemsSource = new[] { "Box", "Sphere", "Cylinder", "Cone", "Plane", "Ellipse" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _createNameBox = CreateWorkbenchBox("optional name");
        _createSizeXBox = CreateWorkbenchBox("x / radius");
        _createSizeYBox = CreateWorkbenchBox("y / height");
        _createSizeZBox = CreateWorkbenchBox("z / depth");
        _createColorRBox = CreateWorkbenchBox("r");
        _createColorGBox = CreateWorkbenchBox("g");
        _createColorBBox = CreateWorkbenchBox("b");
        _createColorABox = CreateWorkbenchBox("a");
        _createSizeXBox.Text = "1";
        _createSizeYBox.Text = "1";
        _createSizeZBox.Text = "1";
        _createColorRBox.Text = "0.35";
        _createColorGBox.Text = "0.65";
        _createColorBBox.Text = "1";
        _createColorABox.Text = "1";
        _createObjectButton = new Button { Content = "Create" };
        _createObjectButton.Click += (_, _) => CreateObjectFromWorkbench();
        _duplicateObjectButton = new Button { Content = "Duplicate" };
        _duplicateObjectButton.Click += (_, _) => DuplicateSelectedObject();
        _deleteObjectButton = new Button { Content = "Delete / hide" };
        _deleteObjectButton.Click += (_, _) => DeleteOrHideSelectedObject();
        _clearWorkbenchButton = new Button { Content = "Clear created" };
        _clearWorkbenchButton.Click += (_, _) => ClearWorkbenchObjects();

        _ghostUnselectedCheckBox = new CheckBox { Content = "Ghost unselected" };
        _ghostUnselectedCheckBox.Click += OnDebugVisualModeChanged;
        _hideUnselectedCheckBox = new CheckBox { Content = "Hide unselected" };
        _hideUnselectedCheckBox.Click += OnDebugVisualModeChanged;
        _showOnlyWorkbenchCheckBox = new CheckBox { Content = "Only workbench objects" };
        _showOnlyWorkbenchCheckBox.Click += OnDebugVisualModeChanged;
        _keepSelectionPinnedCheckBox = new CheckBox { Content = "Pin selection while filtering" };
        _soloSelectedButton = new Button { Content = "Solo" };
        _soloSelectedButton.Click += (_, _) => SoloSelectedObject();
        _resetDebugViewButton = new Button { Content = "Reset view modes" };
        _resetDebugViewButton.Click += (_, _) => ResetDebugVisualModes();
        _copySceneSnippetButton = new Button { Content = "Copy scene code" };
        _copySceneSnippetButton.Click += async (_, _) => await CopySceneSnippetAsync();
        _copyDiagnosticsButton = new Button { Content = "Copy diagnostics" };
        _copyDiagnosticsButton.Click += async (_, _) => await CopyDiagnosticsAsync();
        _exportSourceButton = new Button { Content = "Export code to source (experimental)" };
        _exportSourceButton.Click += async (_, _) => await ExportSourceExperimentalAsync();
        _showBasisCheckBox = new CheckBox { Content = "Show RGB basis axes", IsChecked = true };
        _showBasisCheckBox.Click += (_, _) => UpdateSpaceGuides();
        _showGroundGridCheckBox = new CheckBox { Content = "Show ground grid", IsChecked = true };
        _showGroundGridCheckBox.Click += (_, _) => UpdateSpaceGuides();
        _showDebugOverlayCheckBox = new CheckBox { Content = "Show debug overlay", IsChecked = true };
        _showDebugOverlayCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _showBoundsCheckBox = new CheckBox { Content = "Show object edges / bounds", IsChecked = true };
        _showBoundsCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _showCollidersCheckBox = new CheckBox { Content = "Show colliders" };
        _showCollidersCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _showPickingRayCheckBox = new CheckBox { Content = "Show picking ray" };
        _showPickingRayCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _enablePhysicsCheckBox = new CheckBox { Content = "Enable physics" };
        _enablePhysicsCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _cameraNearBox = CreateSceneBox("near");
        _cameraFarBox = CreateSceneBox("far");
        _drawDistanceBox = CreateSceneBox("draw");
        _distanceFadeBox = CreateSceneBox("fade");
        _enableDistanceFadeCheckBox = new CheckBox { Content = "Distance fade" };
        _enableDistanceFadeCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _enableHighScaleLodCheckBox = new CheckBox { Content = "High-scale LOD" };
        _enableHighScaleLodCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _adaptivePerformanceCheckBox = new CheckBox { Content = "Adaptive performance" };
        _adaptivePerformanceCheckBox.Click += (_, _) => ApplySceneSettingsFromControls();
        _directionalLightEnabledCheckBox = new CheckBox { Content = "Directional light" };
        _directionalLightIntensityBox = CreateSceneBox("intensity");
        _directionalLightXBox = CreateSceneBox("x");
        _directionalLightYBox = CreateSceneBox("y");
        _directionalLightZBox = CreateSceneBox("z");
        _pointLightEnabledCheckBox = new CheckBox { Content = "Point light" };
        _pointLightIntensityBox = CreateSceneBox("intensity");
        _pointLightRangeBox = CreateSceneBox("range");
        _pointLightXBox = CreateSceneBox("x");
        _pointLightYBox = CreateSceneBox("y");
        _pointLightZBox = CreateSceneBox("z");
        _applySceneSettingsButton = new Button { Content = "Apply scene settings" };
        _applySceneSettingsButton.Click += (_, _) => ApplySceneSettingsFromControls();
        _rigidbodyCheckBox = CreateEditorCheckBox("Rigidbody");
        _rigidbodyKinematicCheckBox = CreateEditorCheckBox("Kinematic");
        _rigidbodyGravityCheckBox = CreateEditorCheckBox("Gravity");
        _rigidbodyMassBox = CreateEditorBox("mass");
        _rigidbodyFrictionBox = CreateEditorBox("friction");
        _rigidbodyRestitutionBox = CreateEditorBox("bounce");
        _spaceReadoutText = new TextBlock
        {
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 11d,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 220, 255)),
            Text = "Camera/pointer: --"
        };
        _viewport.PointerMoved += OnViewportPointerMoved;
        _viewport.PointerExited += (_, _) => UpdateSpaceReadout(null, null);
        _viewport.SelectionChanged += OnViewportSelectionChanged;
        _sourceTargetBox = new TextBox
        {
            IsReadOnly = true,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 11d,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 42d
        };

        _primitiveTypeText = new TextBlock
        {
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 12d,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 214, 222))
        };
        _primitiveABox = CreateEditorBox("a");
        _primitiveBBox = CreateEditorBox("b");
        _primitiveCBox = CreateEditorBox("c");
        _primitiveSegmentsABox = CreateEditorBox("segments");
        _primitiveSegmentsBBox = CreateEditorBox("rings");

        _eventTypeBox = new ComboBox
        {
            ItemsSource = new[] { "Clicked", "PointerPressed", "PointerReleased", "PointerEntered", "PointerExited" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _eventTypeBox.SelectionChanged += (_, _) => LoadEventEditorFromSelection();
        _eventCodeBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 12d,
            MinHeight = 96d,
            Watermark = "// C# event body. Export writes this into a generated handler.\n// Example: e.Target.Material.BaseColor = ColorRgba.Red;"
        };
        _eventCodeBox.TextChanged += (_, _) => StoreEventEditorForSelection();
        _eventCodeBox.LostFocus += (_, _) => StoreEventEditorForSelection();
        _eventHintsBox = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 11d,
            MinHeight = 132d,
            Text = _eventHintsText
        };
        _copyEventSnippetButton = new Button { Content = "Copy event code" };
        _copyEventSnippetButton.Click += async (_, _) => await CopyEventSnippetAsync();

        _showLightGizmosCheckBox = new CheckBox { Content = "Show light gizmos", IsChecked = true };
        _showLightGizmosCheckBox.Click += (_, _) => UpdateSpaceGuides();
        AttachSceneLiveEditor(_directionalLightIntensityBox);
        AttachSceneLiveEditor(_directionalLightXBox);
        AttachSceneLiveEditor(_directionalLightYBox);
        AttachSceneLiveEditor(_directionalLightZBox);
        AttachSceneLiveEditor(_pointLightIntensityBox);
        AttachSceneLiveEditor(_pointLightRangeBox);
        AttachSceneLiveEditor(_pointLightXBox);
        AttachSceneLiveEditor(_pointLightYBox);
        AttachSceneLiveEditor(_pointLightZBox);
        AttachSceneLiveEditor(_cameraNearBox);
        AttachSceneLiveEditor(_cameraFarBox);
        AttachSceneLiveEditor(_drawDistanceBox);
        AttachSceneLiveEditor(_distanceFadeBox);

        _toggleLeftPanelButton = new Button { Content = "Left" };
        _toggleRightPanelButton = new Button { Content = "Right" };
        _toggleLeftPanelButton.Click += (_, _) => ToggleLeftPanel();
        _toggleRightPanelButton.Click += (_, _) => ToggleRightPanel();

        _highScalePanel = new Border
        {
            IsVisible = false,
            Padding = new Thickness(8d),
            Margin = new Thickness(0d, 8d, 0d, 0d),
            CornerRadius = new CornerRadius(5d),
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            Child = new StackPanel
            {
                Spacing = 6d,
                Children =
                {
                    SectionTitle("High-scale LOD"),
                    VectorRow("Distances", _lodDetailedBox, _lodSimplifiedBox, _lodProxyBox),
                    LabeledEditor("Draw distance", _lodDrawBox),
                    LabeledEditor("Fade distance", _lodFadeBox),
                    _lodBillboardCheckBox
                }
            }
        };

        _primitivePanel = new Border
        {
            IsVisible = false,
            Padding = new Thickness(8d),
            Margin = new Thickness(0d, 8d, 0d, 0d),
            CornerRadius = new CornerRadius(5d),
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            Child = new StackPanel
            {
                Spacing = 6d,
                Children =
                {
                    SectionTitle("Primitive geometry"),
                    _primitiveTypeText,
                    VectorRow("Size", _primitiveABox, _primitiveBBox, _primitiveCBox),
                    VectorRow("Tessellation", _primitiveSegmentsABox, _primitiveSegmentsBBox)
                }
            }
        };

        _previewSelector.IsVisible = false;

        var leftContent = new StackPanel
        {
            Spacing = 8d,
            Margin = new Thickness(8d),
            Children =
            {
                new TextBlock { Text = "3D Debugger", FontWeight = FontWeight.SemiBold, FontSize = 15d },
                _previewSelector,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6d,
                    Children = { _refreshButton, frameButton }
                },
                BuildWorkbenchCreatePanel(),
                BuildDebuggerToolsPanel(),
                _sceneSummaryText,
                new TextBlock { Text = "Objects / parts", FontWeight = FontWeight.SemiBold },
                _objectFilterBox,
                _partList
            }
        };

        _leftPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(12, 12, 12)),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = leftContent
            }
        };

        var inspectorPanel = new StackPanel
        {
            Spacing = 6d,
            Margin = new Thickness(8d),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                _selectionHeaderText,
                _selectionDetailsText,
                CollapsibleSection("Identity", true,
                    LabeledEditor("Name", _nameBox),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10d, Children = { _visibleCheckBox, _pickableCheckBox } },
                    _manipulationCheckBox),
                CollapsibleSection("Transform", true,
                    VectorRow("Position", _positionXBox, _positionYBox, _positionZBox),
                    VectorRow("Rotation °", _rotationXBox, _rotationYBox, _rotationZBox),
                    VectorRow("Scale", _scaleXBox, _scaleYBox, _scaleZBox)),
                CollapsibleSection("Material", true,
                    VectorRow("RGBA", _colorRBox, _colorGBox, _colorBBox, _colorABox),
                    LabeledEditor("Opacity", _opacityBox),
                    LabeledEditor("Lighting", _lightingBox),
                    LabeledEditor("Surface", _surfaceBox),
                    LabeledEditor("Cull", _cullBox)),
                CollapsibleSection("Physics", false,
                    _rigidbodyCheckBox,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8d, Children = { _rigidbodyKinematicCheckBox, _rigidbodyGravityCheckBox } },
                    LabeledEditor("Mass", _rigidbodyMassBox),
                    VectorRow("Friction / bounce", _rigidbodyFrictionBox, _rigidbodyRestitutionBox)),
                CollapsibleSection("Events", false,
                    LabeledEditor("Event", _eventTypeBox),
                    new TextBlock { Text = "Stored as C# handler body and exported with Build(...). Runtime preview logs the event; arbitrary code is not executed in-process.", TextWrapping = TextWrapping.Wrap, FontSize = 11d, Foreground = new SolidColorBrush(Color.FromRgb(190, 196, 210)) },
                    _eventCodeBox,
                    new TextBlock { Text = "Engine API hints (auto-generated from engine sources)", FontWeight = FontWeight.SemiBold, FontSize = 11d },
                    _eventHintsBox,
                    _copyEventSnippetButton),
                _primitivePanel,
                _highScalePanel,
                _autoApplyCheckBox,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8d, Children = { _applyButton, _resetButton, _copySnippetButton } },
                _statusText,
                CollapsibleSection("C# snippet", false, _snippetBox),
                BuildSourceGenerationPanel()
            }
        };

        _rightPane = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = inspectorPanel
            }
        };

        _leftColumn = new ColumnDefinition(320, GridUnitType.Pixel) { MinWidth = 0d, MaxWidth = 640d };
        _leftSplitterColumn = new ColumnDefinition(5, GridUnitType.Pixel);
        _rightSplitterColumn = new ColumnDefinition(5, GridUnitType.Pixel);
        _rightColumn = new ColumnDefinition(420, GridUnitType.Pixel) { MinWidth = 0d, MaxWidth = 760d };

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions { _leftColumn, _leftSplitterColumn, new ColumnDefinition(1, GridUnitType.Star), _rightSplitterColumn, _rightColumn }
        };

        var leftSplitter = new GridSplitter
        {
            Width = 5d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
        };
        var rightSplitter = new GridSplitter
        {
            Width = 5d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
        };

        Grid.SetColumn(_leftPane, 0);
        Grid.SetColumn(leftSplitter, 1);
        Grid.SetColumn(_viewport, 2);
        Grid.SetColumn(rightSplitter, 3);
        Grid.SetColumn(_rightPane, 4);
        contentGrid.Children.Add(_leftPane);
        contentGrid.Children.Add(leftSplitter);
        contentGrid.Children.Add(_viewport);
        contentGrid.Children.Add(rightSplitter);
        contentGrid.Children.Add(_rightPane);

        var viewMenu = new MenuItem { Header = "_View" };
        var toggleLeftMenu = new MenuItem { Header = "Toggle left panel" };
        toggleLeftMenu.Click += (_, _) => ToggleLeftPanel();
        var toggleRightMenu = new MenuItem { Header = "Toggle inspector" };
        toggleRightMenu.Click += (_, _) => ToggleRightPanel();
        var toggleOverlayMenu = new MenuItem { Header = "Toggle debug overlay" };
        toggleOverlayMenu.Click += (_, _) =>
        {
            _showDebugOverlayCheckBox.IsChecked = _showDebugOverlayCheckBox.IsChecked != true;
            ApplySceneSettingsFromControls();
        };
        viewMenu.ItemsSource = new[] { toggleLeftMenu, toggleRightMenu, toggleOverlayMenu };
        var toolsMenu = new MenuItem { Header = "_Tools" };
        var copySceneMenu = new MenuItem { Header = "Copy scene code" };
        copySceneMenu.Click += async (_, _) => await CopySceneSnippetAsync();
        var copyDiagnosticsMenu = new MenuItem { Header = "Copy diagnostics" };
        copyDiagnosticsMenu.Click += async (_, _) => await CopyDiagnosticsAsync();
        toolsMenu.ItemsSource = new[] { copySceneMenu, copyDiagnosticsMenu };
        var topMenu = new Menu { ItemsSource = new[] { viewMenu, toolsMenu } };
        var topBar = new DockPanel
        {
            LastChildFill = false,
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
            Children =
            {
                topMenu,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 6d,
                    Margin = new Thickness(8d, 3d),
                    Children = { _toggleLeftPanelButton, _toggleRightPanelButton }
                }
            }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { topBar, contentGrid }
        };
        Grid.SetRow(contentGrid, 1);
        Content = root;
    }

    public event EventHandler? RefreshRequested;

    public Scene3DControl Viewport => _viewport;

    private void ToggleLeftPanel()
    {
        var collapsed = _leftColumn.Width.Value <= 0.5d;
        _leftPane.IsVisible = collapsed;
        _leftColumn.Width = collapsed ? new GridLength(320d, GridUnitType.Pixel) : new GridLength(0d, GridUnitType.Pixel);
        _leftSplitterColumn.Width = collapsed ? new GridLength(5d, GridUnitType.Pixel) : new GridLength(0d, GridUnitType.Pixel);
    }

    private void ToggleRightPanel()
    {
        var collapsed = _rightColumn.Width.Value <= 0.5d;
        _rightPane.IsVisible = collapsed;
        _rightColumn.Width = collapsed ? new GridLength(420d, GridUnitType.Pixel) : new GridLength(0d, GridUnitType.Pixel);
        _rightSplitterColumn.Width = collapsed ? new GridLength(5d, GridUnitType.Pixel) : new GridLength(0d, GridUnitType.Pixel);
    }

    public void SetSourceGenerationContext(string? assemblyPath, string? typeFullName, string? projectPath = null)
    {
        _sourceAssemblyPath = string.IsNullOrWhiteSpace(assemblyPath) ? null : Path.GetFullPath(assemblyPath);
        _sourceTypeFullName = string.IsNullOrWhiteSpace(typeFullName) ? null : typeFullName;
        _sourceProjectPath = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFullPath(projectPath);
        _sourcePatchDirectory = ResolveSourcePatchDirectory(_sourceAssemblyPath, _sourceProjectPath);
        _sourceTargetInfo = ResolveSourceTarget(_sourcePatchDirectory, _sourceTypeFullName);
        if (_sourceTargetBox is not null)
        {
            _sourceTargetBox.Text = _sourceTargetInfo is null
                ? "Source target: class source not found. Snippet copy still works."
                : $"Source target: {_sourceTargetInfo.FilePath}\nClass: {_sourceTargetInfo.ClassName} @ line {_sourceTargetInfo.Line}, method: {(_sourceTargetInfo.HasBuildMethod ? "Build(...)" : "none; Build(...) will be inserted")}";
        }

        RefreshEventHints();
    }

    public void SetSourceExportHandler(Func<DebuggerSourceExportRequest, Task<DebuggerSourceExportResult>>? sourceExportHandler)
    {
        _sourceExportHandler = sourceExportHandler;
    }

    public IReadOnlyList<PreviewScene3D> Previews
    {
        get => _previews;
        set
        {
            _previews = value ?? Array.Empty<PreviewScene3D>();
            _updatingPreviewSelector = true;
            try
            {
                _previewSelector.ItemsSource = _previews.Select(p => p.Name).ToArray();
                _previewSelector.SelectedIndex = _previews.Count > 0 ? 0 : -1;
                _previewSelector.IsVisible = _previews.Count > 1;
            }
            finally
            {
                _updatingPreviewSelector = false;
            }

            if (_previews.Count > 0)
            {
                SetPreview(_previews[0]);
            }
            else
            {
                ClearPreview();
            }
        }
    }

    public void SetPreview(PreviewScene3D preview)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        RestoreDebugVisuals();
        _viewport.Scene = preview.Scene;
        UnlockEditableCompositeParts(preview.Scene);
        ApplyDebuggerSceneDefaults(preview.Scene);
        _debugVisualStates.Clear();
        var report = PreviewComplexityReport3D.Analyze(preview.Scene);
        UpdateSpaceGuides();
        LoadSceneSettingsFromScene();
        RefreshEventHints();
        UpdateDebuggerPhysicsPumpState();
        RebuildObjectList(null);
        _sceneSummaryText.Text = $"Preview: {preview.Name}\n" + report.Summary;
        SetStatus("Select an object, create a workbench object, or edit values and copy snippet back into source code.", isError: false);
        ClearInspectorSelection();
    }

    public void ClearPreview()
    {
        foreach (var entry in _listedObjects)
        {
            entry.Object.IsSelected = false;
        }

        RestoreDebugVisuals();
        _viewport.Scene = PreviewScene3D.CreateDefaultScene();
        ApplyDebuggerSceneDefaults(_viewport.Scene);
        _debugVisualStates.Clear();
        UpdateSpaceGuides();
        LoadSceneSettingsFromScene();
        RefreshEventHints();
        UpdateDebuggerPhysicsPumpState();
        _listedObjects.Clear();
        _partList.ItemsSource = Array.Empty<PreviewObjectEntry>();
        _partList.SelectedIndex = -1;
        _sceneSummaryText.Text = "No preview";
        ClearInspectorSelection();
    }

    public void SetError(string message)
    {
        ClearPreview();
        _selectionHeaderText.Text = "Preview error";
        _selectionDetailsText.Text = string.IsNullOrWhiteSpace(message) ? "Preview error" : message;
        SetStatus("Preview load failed.", isError: true);
    }

    private void OnPreviewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPreviewSelector)
        {
            return;
        }

        var index = _previewSelector.SelectedIndex;
        if (index >= 0 && index < _previews.Count)
        {
            SetPreview(_previews[index]);
        }
    }

    private static void UnlockEditableCompositeParts(Scene3D scene)
    {
        foreach (var root in scene.Objects)
        {
            UnlockEditableCompositeParts(root);
        }
    }

    private static void UnlockEditableCompositeParts(Object3D obj)
    {
        if (obj is CompositeObject3D composite)
        {
            // In debugger mode the composite root must not capture selection/manipulation for all children.
            // Otherwise picking normalizes every child hit back to the root and individual details are not editable.
            composite.IsManipulationEnabled = false;
            foreach (var child in composite.Children)
            {
                child.IsPickable = true;
                child.IsManipulationEnabled = true;
                UnlockEditableCompositeParts(child);
            }
        }
    }

    private void RebuildObjectListPreservingSelection()
    {
        RebuildObjectList(_selectedObject);
    }

    private void RebuildObjectList(Object3D? preferredSelection)
    {
        _listedObjects = BuildObjectEntries(_viewport.Scene, _objectFilterBox.Text);
        if (_showOnlyWorkbenchCheckBox?.IsChecked == true)
        {
            _listedObjects = _listedObjects
                .Where(e => ReferenceEquals(e.Object.DataContext, DebugWorkbenchTag.Instance))
                .ToList();
        }
        _partList.ItemsSource = _listedObjects;

        var selectedIndex = preferredSelection is null
            ? -1
            : _listedObjects.FindIndex(e => ReferenceEquals(e.Object, preferredSelection) || e.Object.Id == preferredSelection.Id);
        _partList.SelectedIndex = selectedIndex;
    }

    private static List<PreviewObjectEntry> BuildObjectEntries(Scene3D scene, string? filter)
    {
        var entries = new List<PreviewObjectEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

        foreach (var root in scene.Objects)
        {
            if (ReferenceEquals(root.DataContext, DebugGuideTag.Instance))
            {
                continue;
            }

            AddObject(entries, seen, root, depth: 0, path: root.Name, normalizedFilter);
        }

        return entries;
    }

    private static void AddObject(List<PreviewObjectEntry> entries, HashSet<string> seen, Object3D obj, int depth, string path, string? filter)
    {
        if (ReferenceEquals(obj.DataContext, DebugGuideTag.Instance))
        {
            return;
        }

        if (seen.Add(obj.Id) && MatchesFilter(obj, path, filter))
        {
            entries.Add(new PreviewObjectEntry(obj, depth, path));
        }

        if (obj is not CompositeObject3D composite)
        {
            return;
        }

        foreach (var child in composite.Children)
        {
            var childPath = path + "/" + child.Name;
            AddObject(entries, seen, child, depth + 1, childPath, filter);
        }
    }

    private static bool MatchesFilter(Object3D obj, string path, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               obj.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               obj.GetType().Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               obj.Id.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void OnPartSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_partList.SelectedItem is not PreviewObjectEntry entry)
        {
            ClearInspectorSelection();
            return;
        }

        SelectObject(entry.Object);
    }

    private void OnViewportSelectionChanged(object? sender, SceneSelectionChangedEventArgs e)
    {
        var selected = e.NewSelection;
        if (selected is null || ReferenceEquals(selected.DataContext, DebugGuideTag.Instance))
        {
            return;
        }

        var index = _listedObjects.FindIndex(entry => ReferenceEquals(entry.Object, selected) || entry.Object.Id == selected.Id);
        if (index < 0)
        {
            RebuildObjectList(selected);
            index = _listedObjects.FindIndex(entry => ReferenceEquals(entry.Object, selected) || entry.Object.Id == selected.Id);
        }

        if (index >= 0 && _partList.SelectedIndex != index)
        {
            _partList.SelectedIndex = index;
            return;
        }

        SelectObject(selected);
    }

    private void SelectObject(Object3D selected)
    {
        _selectionService.Select(selected, _listedObjects.Select(entry => entry.Object));
        _selectedObject = _selectionService.SelectedObject;

        LoadInspectorFromSelection();
        ApplyDebugVisualModes();
    }

    private void ClearInspectorSelection()
    {
        _selectionService.Clear(_listedObjects.Select(entry => entry.Object));
        _selectedObject = null;
        _updatingInspector = true;
        try
        {
            _selectionHeaderText.Text = "No selection";
            _selectionDetailsText.Text = "Select an object from the left list.";
            _nameBox.Text = string.Empty;
            _snippetBox.Text = string.Empty;
            _eventCodeBox.Text = string.Empty;
            _primitivePanel.IsVisible = false;
            _highScalePanel.IsVisible = false;
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private void LoadInspectorFromSelection()
    {
        var obj = _selectedObject;
        if (obj is null)
        {
            ClearInspectorSelection();
            return;
        }

        _updatingInspector = true;
        try
        {
            _selectionHeaderText.Text = obj.Name;
            _selectionDetailsText.Text = BuildSelectionDetails(obj);
            _nameBox.Text = obj.Name;
            _visibleCheckBox.IsChecked = obj.IsVisible;
            _pickableCheckBox.IsChecked = obj.IsPickable;
            _manipulationCheckBox.IsChecked = obj.IsManipulationEnabled;
            SetVector(_positionXBox, _positionYBox, _positionZBox, obj.Position);
            SetVector(_rotationXBox, _rotationYBox, _rotationZBox, obj.RotationDegrees);
            SetVector(_scaleXBox, _scaleYBox, _scaleZBox, obj.Scale);
            var color = obj.Material.BaseColor;
            _colorRBox.Text = FormatFloat(color.R);
            _colorGBox.Text = FormatFloat(color.G);
            _colorBBox.Text = FormatFloat(color.B);
            _colorABox.Text = FormatFloat(color.A);
            _opacityBox.Text = FormatFloat(obj.Material.Opacity);
            _lightingBox.SelectedItem = obj.Material.Lighting;
            _surfaceBox.SelectedItem = obj.Material.Surface;
            _cullBox.SelectedItem = obj.Material.CullMode;
            LoadPrimitiveInspector(obj);
            LoadEventEditorFromSelection();

            LoadRigidbodyInspector(obj);

            if (obj is HighScaleInstanceLayer3D layer)
            {
                _highScalePanel.IsVisible = true;
                _lodDetailedBox.Text = FormatFloat(layer.LodPolicy.DetailedDistance);
                _lodSimplifiedBox.Text = FormatFloat(layer.LodPolicy.SimplifiedDistance);
                _lodProxyBox.Text = FormatFloat(layer.LodPolicy.ProxyDistance);
                _lodDrawBox.Text = FormatFloat(layer.LodPolicy.DrawDistance);
                _lodFadeBox.Text = FormatFloat(layer.LodPolicy.FadeDistance);
                _lodBillboardCheckBox.IsChecked = layer.LodPolicy.EnableBillboardFallback;
            }
            else
            {
                _highScalePanel.IsVisible = false;
            }

            UpdateSnippet(obj);
            SetStatus("Values loaded. Edit fields and apply.", isError: false);
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private string BuildSelectionDetails(Object3D obj)
    {
        var bounds = obj.WorldBounds;
        var boundsText = bounds.IsValid
            ? $"{FormatVector(bounds.Min)} - {FormatVector(bounds.Max)}"
            : "empty";

        var path = _listedObjects.FirstOrDefault(e => ReferenceEquals(e.Object, obj))?.Path ?? obj.Name;
        var extra = obj is HighScaleInstanceLayer3D layer
            ? $"\nHighScale instances: {layer.Instances.Count:n0}\nChunks: {layer.Chunks.Chunks.Count:n0}\nLOD version: {layer.LodPolicy.Version}"
            : string.Empty;

        return
            $"Type: {obj.GetType().FullName}\n" +
            $"Path: {path}\n" +
            $"Id: {obj.Id}\n" +
            $"Parent: {obj.Parent?.Name ?? "none"}\n" +
            $"Mesh: {(obj.UseMeshRendering ? obj.GetMesh().ResourceKey : "none")}\n" +
            $"Collider: {obj.Collider?.GetType().Name ?? "none"}\n" +
            $"Rigidbody: {obj.Rigidbody?.GetType().Name ?? "none"}\n" +
            $"Bounds: {boundsText}" +
            extra;
    }

    private bool TryApplyInspectorValues(object? sender, bool rebuildList, bool forceStatus)
    {
        if (_updatingInspector || _selectedObject is null)
        {
            return false;
        }

        var obj = _selectedObject;
        var applyAll = sender is null;
        var applyIdentity = applyAll || IsIdentityEditor(sender);
        var applyTransform = applyAll || IsTransformEditor(sender);
        var applyMaterial = applyAll || IsMaterialEditor(sender);
        var applyPrimitive = applyAll || IsPrimitiveEditor(sender);
        var applyPhysics = applyAll || IsPhysicsEditor(sender);
        var applyHighScale = applyAll || IsHighScaleEditor(sender);
        Vector3 position;
        Vector3 rotation;
        Vector3 scale;

        if (!(applyIdentity || applyTransform || applyMaterial || applyPrimitive || applyPhysics || applyHighScale))
        {
            applyIdentity = applyTransform = applyMaterial = applyPrimitive = applyPhysics = applyHighScale = true;
        }

        if (applyTransform &&
            (!TryReadVector(_positionXBox, _positionYBox, _positionZBox, out position) ||
             !TryReadVector(_rotationXBox, _rotationYBox, _rotationZBox, out rotation) ||
             !TryReadVector(_scaleXBox, _scaleYBox, _scaleZBox, out scale)))
        {
            if (forceStatus)
            {
                SetStatus("Invalid numeric value. Use invariant numbers such as 1.25 or 0.5.", isError: true);
            }

            return false;
        }

        position = obj.Position;
        rotation = obj.RotationDegrees;
        scale = obj.Scale;

        if (applyTransform)
        {
            TryReadVector(_positionXBox, _positionYBox, _positionZBox, out position);
            TryReadVector(_rotationXBox, _rotationYBox, _rotationZBox, out rotation);
            TryReadVector(_scaleXBox, _scaleYBox, _scaleZBox, out scale);
        }

        var color = obj.Material.BaseColor;
        var opacity = obj.Material.Opacity;
        if (applyMaterial)
        {
            if (!TryReadColor(out color) || !TryReadFloat(_opacityBox.Text, out opacity))
            {
                if (forceStatus)
                {
                    SetStatus("Invalid numeric value. Use invariant numbers such as 1.25 or 0.5.", isError: true);
                }

                return false;
            }
        }

        using (_viewport.Scene.BeginUpdate())
        {
            if (applyIdentity)
            {
                var name = _nameBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    obj.Name = name;
                }

                obj.IsVisible = _visibleCheckBox.IsChecked == true;
                obj.IsPickable = _pickableCheckBox.IsChecked == true;
                obj.IsManipulationEnabled = _manipulationCheckBox.IsChecked == true;
            }

            if (applyTransform)
            {
                obj.Position = position;
                obj.RotationDegrees = rotation;
                obj.Scale = scale;
            }

            if (applyMaterial)
            {
                obj.Material.BaseColor = color;
                obj.Material.Opacity = opacity;
                if (_lightingBox.SelectedItem is LightingMode lighting) obj.Material.Lighting = lighting;
                if (_surfaceBox.SelectedItem is SurfaceMode surface) obj.Material.Surface = surface;
                if (_cullBox.SelectedItem is CullMode cull) obj.Material.CullMode = cull;
            }

            if (applyPrimitive)
            {
                TryApplyPrimitiveValues(obj);
            }

            if (applyPhysics)
            {
                ApplyRigidbodyInspector(obj);
            }

            if (applyHighScale && obj is HighScaleInstanceLayer3D layer)
            {
                if (TryReadFloat(_lodDetailedBox.Text, out var detailed)) layer.LodPolicy.DetailedDistance = detailed;
                if (TryReadFloat(_lodSimplifiedBox.Text, out var simplified)) layer.LodPolicy.SimplifiedDistance = simplified;
                if (TryReadFloat(_lodProxyBox.Text, out var proxy)) layer.LodPolicy.ProxyDistance = proxy;
                if (TryReadFloat(_lodDrawBox.Text, out var draw)) layer.LodPolicy.DrawDistance = draw;
                if (TryReadFloat(_lodFadeBox.Text, out var fade)) layer.LodPolicy.FadeDistance = fade;
                layer.LodPolicy.EnableBillboardFallback = _lodBillboardCheckBox.IsChecked == true;
            }
        }

        if (applyPhysics || applyTransform || applyPrimitive)
        {
            ApplyPhysicsImmediately(obj);
        }

        _selectionHeaderText.Text = obj.Name;
        _selectionDetailsText.Text = BuildSelectionDetails(obj);
        UpdateSnippet(obj);
        if (rebuildList)
        {
            RebuildObjectList(obj);
        }

        ApplyDebugVisualModes();
        SetStatus("Applied to preview scene. Copy snippet to port values into source code.", isError: false);
        return true;
    }

    private void LoadEventEditorFromSelection()
    {
        if (_updatingInspector)
        {
            return;
        }

        var obj = _selectedObject;
        if (obj is null)
        {
            return;
        }

        _updatingInspector = true;
        try
        {
            var eventName = ResolveSelectedEventName();
            _eventCodeBox.Text = _eventBindings.TryGetValue(MakeEventBindingKey(obj, eventName), out var binding)
                ? binding.Body
                : DefaultEventBody(eventName);
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private void StoreEventEditorForSelection()
    {
        if (_updatingInspector || _selectedObject is null)
        {
            return;
        }

        var eventName = ResolveSelectedEventName();
        var body = _eventCodeBox.Text ?? string.Empty;
        var key = MakeEventBindingKey(_selectedObject, eventName);
        if (string.IsNullOrWhiteSpace(body) || string.Equals(body.Trim(), DefaultEventBody(eventName).Trim(), StringComparison.Ordinal))
        {
            _eventBindings.Remove(key);
        }
        else
        {
            _eventBindings[key] = new DebugEventBinding(_selectedObject.Id, eventName, body);
            AttachRuntimeEventLogger(_selectedObject);
        }

        UpdateSnippet(_selectedObject);
    }

    private async Task CopyEventSnippetAsync()
    {
        var obj = _selectedObject;
        if (obj is null)
        {
            SetStatus("Select an object before copying event code.", isError: true);
            return;
        }

        StoreEventEditorForSelection();
        var text = BuildEventHandlerSource(obj, ResolveSelectedEventName(), ResolveEventHandlerName("Selected", ResolveSelectedEventName()), _eventCodeBox.Text ?? string.Empty, "    ");
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
            SetStatus("Event handler snippet copied.", isError: false);
        }
    }

    private void AttachRuntimeEventLogger(Object3D obj)
    {
        if (!_runtimeEventHandlersAttached.Add(obj.Id))
        {
            return;
        }

        obj.Clicked += OnDebugRuntimeObjectEvent;
        obj.PointerPressed += OnDebugRuntimeObjectEvent;
        obj.PointerReleased += OnDebugRuntimeObjectEvent;
        obj.PointerEntered += OnDebugRuntimeObjectEvent;
        obj.PointerExited += OnDebugRuntimeObjectEvent;
    }

    private void OnDebugRuntimeObjectEvent(object? sender, ScenePointerEventArgs e)
    {
        if (sender is not Object3D obj)
        {
            return;
        }

        var eventNames = new[] { "Clicked", "PointerPressed", "PointerReleased", "PointerEntered", "PointerExited" };
        var available = eventNames.Any(name => _eventBindings.ContainsKey(MakeEventBindingKey(obj, name)));
        if (available)
        {
            SetStatus($"Event draft exists for {obj.Name}. Export source to compile and execute custom code.", isError: false);
        }
    }

    private string ResolveSelectedEventName() => _eventTypeBox.SelectedItem?.ToString() ?? "Clicked";

    private static string MakeEventBindingKey(Object3D obj, string eventName) => obj.Id + ":" + eventName;

    private static string DefaultEventBody(string eventName)
        => "// C# body for " + eventName + ".\n// Example:\n// e.Target.Material.BaseColor = ColorRgba.Red;";

    private void LoadRigidbodyInspector(Object3D obj)
    {
        var body = obj.Rigidbody;
        _rigidbodyCheckBox.IsChecked = body is not null;
        _rigidbodyMassBox.Text = FormatFloat(body?.Mass ?? 1f);
        _rigidbodyFrictionBox.Text = FormatFloat(body?.Friction ?? 0.55f);
        _rigidbodyRestitutionBox.Text = FormatFloat(body?.Restitution ?? 0.15f);
        _rigidbodyKinematicCheckBox.IsChecked = body?.IsKinematic ?? false;
        _rigidbodyGravityCheckBox.IsChecked = body?.UseGravity ?? true;
    }

    private void ApplyRigidbodyInspector(Object3D obj)
    {
        if (_rigidbodyCheckBox.IsChecked != true)
        {
            obj.Rigidbody = null;
            UpdateDebuggerPhysicsPumpState();
            return;
        }

        if (_enablePhysicsCheckBox.IsChecked != true)
        {
            _enablePhysicsCheckBox.IsChecked = true;
        }

        _debuggerPhysics.EnsureObjectReady(obj);

        var isNewBody = obj.Rigidbody is null;
        var body = obj.Rigidbody ?? new Rigidbody3D();
        if (TryReadFloat(_rigidbodyMassBox.Text, out var mass)) body.Mass = MathF.Max(0.001f, mass);
        if (TryReadFloat(_rigidbodyFrictionBox.Text, out var friction)) body.Friction = MathF.Max(0f, friction);
        if (TryReadFloat(_rigidbodyRestitutionBox.Text, out var restitution)) body.Restitution = MathF.Max(0f, restitution);
        body.IsKinematic = _rigidbodyKinematicCheckBox.IsChecked == true;
        body.UseGravity = _rigidbodyGravityCheckBox.IsChecked == true;
        if (isNewBody)
        {
            body.IsGrounded = false;
        }

        obj.Rigidbody = body;
        _viewport.Scene.Invalidate();
        UpdateDebuggerPhysicsPumpState();
    }

    private void AttachSceneLiveEditor(TextBox box)
    {
        box.TextChanged += (_, _) =>
        {
            if (!_updatingSceneSettings) ApplySceneSettingsFromControls(setStatus: false);
        };
        box.LostFocus += (_, _) =>
        {
            if (!_updatingSceneSettings) ApplySceneSettingsFromControls(setStatus: false);
        };
    }

    private void LoadSceneSettingsFromScene()
    {
        var scene = _viewport.Scene;
        _updatingSceneSettings = true;
        try
        {
            _showDebugOverlayCheckBox.IsChecked = _viewport.ShowPerformanceMetrics;
            _showBoundsCheckBox.IsChecked = scene.Debug.ShowBounds;
            _showCollidersCheckBox.IsChecked = scene.Debug.ShowColliders;
            _showPickingRayCheckBox.IsChecked = scene.Debug.ShowPickingRay;
            _enablePhysicsCheckBox.IsChecked = scene.PhysicsCore is not null;
            _cameraNearBox.Text = FormatFloat(scene.Camera.NearPlane);
            _cameraFarBox.Text = FormatFloat(scene.Camera.FarPlane);
            _drawDistanceBox.Text = FormatFloat(scene.Performance.DrawDistance);
            _distanceFadeBox.Text = FormatFloat(scene.Performance.DistanceFadeBand);
            _enableDistanceFadeCheckBox.IsChecked = scene.Performance.EnableDistanceFade;
            _enableHighScaleLodCheckBox.IsChecked = scene.Performance.EnableHighScaleLod;
            _adaptivePerformanceCheckBox.IsChecked = scene.Performance.AdaptivePerformanceEnabled;

            var directional = scene.Lights.FirstOrDefault();
            _directionalLightEnabledCheckBox.IsChecked = directional?.IsEnabled ?? false;
            _directionalLightIntensityBox.Text = FormatFloat(directional?.Intensity ?? 1f);
            var dir = directional?.Direction ?? Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f));
            SetVector(_directionalLightXBox, _directionalLightYBox, _directionalLightZBox, dir);

            var point = scene.PointLights.FirstOrDefault();
            _pointLightEnabledCheckBox.IsChecked = point?.IsEnabled ?? false;
            _pointLightIntensityBox.Text = FormatFloat(point?.Intensity ?? 2.5f);
            _pointLightRangeBox.Text = FormatFloat(point?.Range ?? 12f);
            SetVector(_pointLightXBox, _pointLightYBox, _pointLightZBox, point?.Position ?? new Vector3(0f, 4f, -2f));
        }
        finally
        {
            _updatingSceneSettings = false;
        }
    }

    private void ApplySceneSettingsFromControls(bool setStatus = true)
    {
        if (_updatingSceneSettings)
        {
            return;
        }

        var scene = _viewport.Scene;
        _viewport.ShowPerformanceMetrics = _showDebugOverlayCheckBox.IsChecked == true;
        scene.Debug.ShowPerformanceMetrics = _viewport.ShowPerformanceMetrics;
        scene.Debug.ShowBounds = _showBoundsCheckBox.IsChecked == true;
        scene.Debug.ShowColliders = _showCollidersCheckBox.IsChecked == true;
        scene.Debug.ShowPickingRay = _showPickingRayCheckBox.IsChecked == true;
        scene.PhysicsCore = _enablePhysicsCheckBox.IsChecked == true ? scene.PhysicsCore ?? new BasicPhysicsCore() : null;
        if (TryReadFloat(_cameraNearBox.Text, out var nearPlane)) scene.Camera.NearPlane = nearPlane;
        if (TryReadFloat(_cameraFarBox.Text, out var farPlane)) scene.Camera.FarPlane = farPlane;
        if (TryReadFloat(_drawDistanceBox.Text, out var drawDistance)) scene.Performance.DrawDistance = MathF.Max(1f, drawDistance);
        if (TryReadFloat(_distanceFadeBox.Text, out var fadeBand)) scene.Performance.DistanceFadeBand = MathF.Max(0f, fadeBand);
        scene.Performance.EnableDistanceFade = _enableDistanceFadeCheckBox.IsChecked == true;
        scene.Performance.EnableHighScaleLod = _enableHighScaleLodCheckBox.IsChecked == true;
        scene.Performance.AdaptivePerformanceEnabled = _adaptivePerformanceCheckBox.IsChecked == true;

        var directional = EnsureDirectionalLight(scene);
        directional.IsEnabled = _directionalLightEnabledCheckBox.IsChecked == true;
        if (TryReadFloat(_directionalLightIntensityBox.Text, out var dirIntensity)) directional.Intensity = dirIntensity;
        if (TryReadVector(_directionalLightXBox, _directionalLightYBox, _directionalLightZBox, out var dir) && dir.LengthSquared() > 0.000001f) directional.Direction = Vector3.Normalize(dir);

        var point = EnsurePointLight(scene);
        point.IsEnabled = _pointLightEnabledCheckBox.IsChecked == true;
        if (TryReadFloat(_pointLightIntensityBox.Text, out var pointIntensity)) point.Intensity = pointIntensity;
        if (TryReadFloat(_pointLightRangeBox.Text, out var pointRange)) point.Range = MathF.Max(0.001f, pointRange);
        if (TryReadVector(_pointLightXBox, _pointLightYBox, _pointLightZBox, out var pointPosition)) point.Position = pointPosition;

        UpdateSpaceGuides();
        if (scene.PhysicsCore is not null)
        {
            ApplyPhysicsImmediately(_selectedObject);
        }

        UpdateDebuggerPhysicsPumpState();
        scene.Invalidate();
        if (setStatus)
        {
            SetStatus("Scene/debug settings applied live.", isError: false);
        }
    }

    private static void ApplyDebuggerSceneDefaults(Scene3D scene)
    {
        scene.Debug.ShowBounds = true;
        scene.PhysicsCore = null;
        if (scene.Performance.DrawDistance < 100f)
        {
            scene.Performance.DrawDistance = 100f;
        }

        if (scene.Camera.FarPlane < scene.Performance.DrawDistance)
        {
            scene.Camera.FarPlane = scene.Performance.DrawDistance;
        }
    }

    private static DirectionalLight3D EnsureDirectionalLight(Scene3D scene)
    {
        var light = scene.Lights.FirstOrDefault();
        if (light is not null) return light;
        return scene.AddLight(new DirectionalLight3D());
    }

    private static PointLight3D EnsurePointLight(Scene3D scene)
    {
        var light = scene.PointLights.FirstOrDefault();
        if (light is not null) return light;
        return scene.AddLight(new PointLight3D());
    }

    private bool IsIdentityEditor(object? sender)
        => IsOneOf(sender, _nameBox, _visibleCheckBox, _pickableCheckBox, _manipulationCheckBox);

    private bool IsTransformEditor(object? sender)
        => IsOneOf(sender, _positionXBox, _positionYBox, _positionZBox, _rotationXBox, _rotationYBox, _rotationZBox, _scaleXBox, _scaleYBox, _scaleZBox);

    private bool IsMaterialEditor(object? sender)
        => IsOneOf(sender, _colorRBox, _colorGBox, _colorBBox, _colorABox, _opacityBox, _lightingBox, _surfaceBox, _cullBox);

    private bool IsPrimitiveEditor(object? sender)
        => IsOneOf(sender, _primitiveABox, _primitiveBBox, _primitiveCBox, _primitiveSegmentsABox, _primitiveSegmentsBBox);

    private bool IsPhysicsEditor(object? sender)
        => IsOneOf(sender, _rigidbodyCheckBox, _rigidbodyKinematicCheckBox, _rigidbodyGravityCheckBox, _rigidbodyMassBox, _rigidbodyFrictionBox, _rigidbodyRestitutionBox);

    private bool IsHighScaleEditor(object? sender)
        => IsOneOf(sender, _lodDetailedBox, _lodSimplifiedBox, _lodProxyBox, _lodDrawBox, _lodFadeBox, _lodBillboardCheckBox);

    private static bool IsOneOf(object? sender, params object?[] controls)
    {
        foreach (var control in controls)
        {
            if (ReferenceEquals(sender, control))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyPhysicsImmediately(Object3D? selectionToRefresh)
    {
        _debuggerPhysics.StepImmediate(selectionToRefresh);
    }

    private void RefreshSelectionAfterPhysics(Object3D? obj)
    {
        if (obj is null || !ReferenceEquals(obj, _selectedObject))
        {
            return;
        }

        _updatingInspector = true;
        try
        {
            SetVector(_positionXBox, _positionYBox, _positionZBox, obj.Position);
            SetVector(_rotationXBox, _rotationYBox, _rotationZBox, obj.RotationDegrees);
            SetVector(_scaleXBox, _scaleYBox, _scaleZBox, obj.Scale);
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private void UpdateDebuggerPhysicsPumpState()
    {
        _debuggerPhysics.UpdatePumpState();
    }

    private void RefreshEventHints()
    {
        _eventHintsText = BuildEventHints();
        if (_eventHintsBox is not null)
        {
            _eventHintsBox.Text = _eventHintsText;
        }
    }

    private string BuildEventHints()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Generated event hints");
        sb.AppendLine("---------------------");
        sb.AppendLine("Available by default in handlers:");
        sb.AppendLine("- sender: the event source object");
        sb.AppendLine("- e: ScenePointerEventArgs");
        sb.AppendLine("- e.Target: Object3D under the pointer");
        sb.AppendLine();

        var sourceRoot = _sourcePatchDirectory;
        if (!string.IsNullOrWhiteSpace(sourceRoot) && Directory.Exists(sourceRoot))
        {
            AppendPublicMemberHints(sb, sourceRoot, "3DEngine/Core/Interaction/ScenePointerEventArgs.cs", "ScenePointerEventArgs e");
            AppendPublicMemberHints(sb, sourceRoot, "3DEngine/Core/Scene/Object3D.cs", "Object3D");
            AppendPublicMemberHints(sb, sourceRoot, "3DEngine/Core/Materials/Material3D.cs", "Material3D");
            AppendPublicMemberHints(sb, sourceRoot, "3DEngine/Core/Physics/Rigidbody3D.cs", "Rigidbody3D");
            AppendPublicMemberHints(sb, sourceRoot, "3DEngine/Core/Primitives/ColorRgba.cs", "ColorRgba");
        }
        else
        {
            sb.AppendLine("Source-based hints are not available yet. Load the preview from a project/source context to enable them.");
        }

        sb.AppendLine();
        sb.AppendLine("Common examples:");
        sb.AppendLine("e.Target.Material.BaseColor = new ColorRgba(1f, 0.2f, 0.2f, 1f);");
        sb.AppendLine("e.Target.Position += new Vector3(0f, 0.5f, 0f);");
        sb.AppendLine("if (e.Target.Rigidbody is not null) e.Target.Rigidbody.UseGravity = false;");
        return sb.ToString().TrimEnd();
    }

    private static void AppendPublicMemberHints(StringBuilder sb, string sourceRoot, string relativePath, string label)
    {
        var path = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return;
        }

        var text = File.ReadAllText(path);
        var matches = Regex.Matches(text, @"public\s+(?:static\s+)?(?:[\w<>,.?\[\]\s]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>|\()", RegexOptions.Multiline);
        var members = matches
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(name => !string.Equals(name, label, StringComparison.Ordinal) && !string.Equals(name, "Equals", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(18)
            .ToArray();

        if (members.Length == 0)
        {
            return;
        }

        sb.Append(label).Append(": ").AppendLine(string.Join(", ", members));
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (_updatingInspector || _autoApplyCheckBox.IsChecked != true)
        {
            return;
        }

        TryApplyInspectorValues(sender, rebuildList: false, forceStatus: false);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryApplyInspectorValues(sender, rebuildList: true, forceStatus: true);
            e.Handled = true;
        }
    }



    private Control BuildDebuggerToolsPanel()
    {
        return ToolPanel(
            CollapsibleSection("Focus", true,
                new TextBlock
                {
                    Text = "Isolate, ghost, filter and export the current runtime state.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11d,
                    Foreground = new SolidColorBrush(Color.FromRgb(190, 196, 210))
                },
                _ghostUnselectedCheckBox,
                _hideUnselectedCheckBox,
                _showOnlyWorkbenchCheckBox,
                _keepSelectionPinnedCheckBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6d,
                    Children = { _soloSelectedButton, _resetDebugViewButton }
                }),
            CollapsibleSection("Scene", false,
                _showDebugOverlayCheckBox,
                _showBoundsCheckBox,
                _showCollidersCheckBox,
                _showPickingRayCheckBox,
                _enablePhysicsCheckBox,
                VectorRow("Camera near/far", _cameraNearBox, _cameraFarBox),
                VectorRow("Draw/fade", _drawDistanceBox, _distanceFadeBox),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8d, Children = { _enableDistanceFadeCheckBox, _enableHighScaleLodCheckBox, _adaptivePerformanceCheckBox } },
                _directionalLightEnabledCheckBox,
                LabeledEditor("Dir intensity", _directionalLightIntensityBox),
                VectorRow("Dir XYZ", _directionalLightXBox, _directionalLightYBox, _directionalLightZBox),
                _pointLightEnabledCheckBox,
                LabeledEditor("Point intensity", _pointLightIntensityBox),
                LabeledEditor("Point range", _pointLightRangeBox),
                VectorRow("Point XYZ", _pointLightXBox, _pointLightYBox, _pointLightZBox)),
            CollapsibleSection("Space", true,
                _showBasisCheckBox,
                _showGroundGridCheckBox,
                _showLightGizmosCheckBox,
                _spaceReadoutText),
            CollapsibleSection("Export", false,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6d,
                    Children = { _copySceneSnippetButton, _copyDiagnosticsButton }
                }));
    }

    private Control BuildSourceGenerationPanel()
    {
        return ToolPanel(
            CollapsibleSection("Experimental source generation", false,
                new TextBlock
                {
                    Text = "Experimental export. It replaces or inserts only Build(...). A .3ddebugger.bak backup is created before writing.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11d
                },
                _sourceTargetBox,
                _exportSourceButton));
    }

    private Control BuildWorkbenchCreatePanel()
    {
        return ToolPanel(
            CollapsibleSection("Create / debug", true,
                LabeledEditor("Primitive", _createTypeBox),
                LabeledEditor("Name", _createNameBox),
                VectorRow("Size", _createSizeXBox, _createSizeYBox, _createSizeZBox),
                VectorRow("RGBA", _createColorRBox, _createColorGBox, _createColorBBox, _createColorABox),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6d,
                    Children = { _createObjectButton, _duplicateObjectButton }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6d,
                    Children = { _deleteObjectButton, _clearWorkbenchButton }
                }));
    }

    private void CreateObjectFromWorkbench()
    {
        var type = _createTypeBox.SelectedItem?.ToString() ?? "Box";
        var name = _createNameBox.Text?.Trim();
        var size = ReadWorkbenchSize();
        var color = ReadWorkbenchColor();
        var obj = CreatePrimitiveObject(type, size);
        obj.Name = string.IsNullOrWhiteSpace(name) ? NextWorkbenchName(type) : name;
        obj.Material.BaseColor = color;
        obj.Material.Opacity = color.A;
        var selectedBounds = _selectedObject?.WorldBounds;
        obj.Position = selectedBounds.HasValue && selectedBounds.Value.IsValid
            ? selectedBounds.Value.Center + new Vector3(size.X * 1.25f, 0f, 0f)
            : Vector3.Zero;
        obj.DataContext = DebugWorkbenchTag.Instance;

        using (_viewport.Scene.BeginUpdate())
        {
            _viewport.Scene.Add(obj);
        }

        RebuildObjectList(obj);
        SelectObject(obj);
        SetStatus($"Created {obj.GetType().Name}. Adjust values, then Copy code.", isError: false);
    }

    private void DuplicateSelectedObject()
    {
        var selected = _selectedObject;
        if (selected is null)
        {
            SetStatus("Select an object to duplicate.", isError: true);
            return;
        }

        var clone = CloneSupportedObject(selected);
        if (clone is null)
        {
            SetStatus($"Duplication is not supported for {selected.GetType().Name}. Create a primitive instead.", isError: true);
            return;
        }

        clone.Name = selected.Name + " Copy";
        clone.Position = selected.Position + new Vector3(0.6f, 0.25f, -0.6f);
        clone.RotationDegrees = selected.RotationDegrees;
        clone.Scale = selected.Scale;
        clone.IsVisible = selected.IsVisible;
        clone.IsPickable = selected.IsPickable;
        clone.IsManipulationEnabled = selected.IsManipulationEnabled;
        clone.Material.BaseColor = selected.Material.BaseColor;
        clone.Material.Opacity = selected.Material.Opacity;
        clone.Material.Lighting = selected.Material.Lighting;
        clone.Material.Surface = selected.Material.Surface;
        clone.Material.CullMode = selected.Material.CullMode;
        clone.DataContext = DebugWorkbenchTag.Instance;

        using (_viewport.Scene.BeginUpdate())
        {
            _viewport.Scene.Add(clone);
        }

        RebuildObjectList(clone);
        SelectObject(clone);
        SetStatus("Duplicated as standalone workbench object.", isError: false);
    }

    private void DeleteOrHideSelectedObject()
    {
        var selected = _selectedObject;
        if (selected is null)
        {
            SetStatus("Select an object to delete or hide.", isError: true);
            return;
        }

        if (selected.Parent is not null)
        {
            selected.IsVisible = false;
            LoadInspectorFromSelection();
            SetStatus("Selected item is a generated part of a composite. It cannot be removed from source-built composite state here, so it was hidden instead.", isError: false);
            return;
        }

        using (_viewport.Scene.BeginUpdate())
        {
            if (!_viewport.Scene.Remove(selected))
            {
                selected.IsVisible = false;
            }
        }

        _selectedObject = null;
        RebuildObjectList(null);
        ClearInspectorSelection();
        SetStatus("Removed root object from debug scene.", isError: false);
    }

    private void ClearWorkbenchObjects()
    {
        var removable = _viewport.Scene.Objects.Where(o => ReferenceEquals(o.DataContext, DebugWorkbenchTag.Instance)).ToArray();
        if (removable.Length == 0)
        {
            SetStatus("No workbench-created root objects to clear.", isError: false);
            return;
        }

        using (_viewport.Scene.BeginUpdate())
        {
            foreach (var obj in removable)
            {
                _viewport.Scene.Remove(obj);
            }
        }

        _selectedObject = null;
        RebuildObjectList(null);
        ClearInspectorSelection();
        SetStatus($"Removed {removable.Length} workbench-created object(s).", isError: false);
    }

    private Vector3 ReadWorkbenchSize()
    {
        var x = TryReadFloat(_createSizeXBox.Text, out var vx) ? vx : 1f;
        var y = TryReadFloat(_createSizeYBox.Text, out var vy) ? vy : 1f;
        var z = TryReadFloat(_createSizeZBox.Text, out var vz) ? vz : 1f;
        return new Vector3(MathF.Max(0.001f, x), MathF.Max(0.001f, y), MathF.Max(0.001f, z));
    }

    private ColorRgba ReadWorkbenchColor()
    {
        var r = TryReadFloat(_createColorRBox.Text, out var vr) ? Clamp01(vr) : 0.35f;
        var g = TryReadFloat(_createColorGBox.Text, out var vg) ? Clamp01(vg) : 0.65f;
        var b = TryReadFloat(_createColorBBox.Text, out var vb) ? Clamp01(vb) : 1f;
        var a = TryReadFloat(_createColorABox.Text, out var va) ? Clamp01(va) : 1f;
        return new ColorRgba(r, g, b, a);
    }

    private Object3D CreatePrimitiveObject(string type, Vector3 size)
    {
        return type switch
        {
            "Sphere" => new Sphere3D { Radius = MathF.Max(0.001f, size.X), Segments = 32, Rings = 16 },
            "Cylinder" => new Cylinder3D { Radius = MathF.Max(0.001f, size.X), Height = MathF.Max(0.001f, size.Y), Segments = 32 },
            "Cone" => new Cone3D { Radius = MathF.Max(0.001f, size.X), Height = MathF.Max(0.001f, size.Y), Segments = 32 },
            "Plane" => new Plane3D { Width = MathF.Max(0.001f, size.X), Height = MathF.Max(0.001f, size.Z), SegmentsX = 1, SegmentsY = 1 },
            "Ellipse" => new Ellipse3D { Width = MathF.Max(0.001f, size.X), Height = MathF.Max(0.001f, size.Y), Depth = MathF.Max(0.001f, size.Z), Segments = 48 },
            _ => new Box3D { Width = MathF.Max(0.001f, size.X), Height = MathF.Max(0.001f, size.Y), Depth = MathF.Max(0.001f, size.Z) }
        };
    }

    private string NextWorkbenchName(string type)
    {
        var baseName = "Debug " + type;
        var index = 1;
        var existing = new HashSet<string>(_viewport.Scene.Registry.AllObjects.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
        var candidate = baseName;
        while (existing.Contains(candidate))
        {
            candidate = baseName + " " + ++index;
        }

        return candidate;
    }

    private Object3D? CloneSupportedObject(Object3D source)
    {
        return source switch
        {
            Box3D box => new Box3D { Width = box.Width, Height = box.Height, Depth = box.Depth },
            Rectangle3D rect => new Box3D { Width = rect.Width, Height = rect.Height, Depth = rect.Depth },
            Sphere3D sphere => new Sphere3D { Radius = sphere.Radius, Segments = sphere.Segments, Rings = sphere.Rings },
            Cylinder3D cylinder => new Cylinder3D { Radius = cylinder.Radius, Height = cylinder.Height, Segments = cylinder.Segments },
            Cone3D cone => new Cone3D { Radius = cone.Radius, Height = cone.Height, Segments = cone.Segments },
            Plane3D plane => new Plane3D { Width = plane.Width, Height = plane.Height, SegmentsX = plane.SegmentsX, SegmentsY = plane.SegmentsY },
            Ellipse3D ellipse => new Ellipse3D { Width = ellipse.Width, Height = ellipse.Height, Depth = ellipse.Depth, Segments = ellipse.Segments },
            _ => null
        };
    }

    private void LoadPrimitiveInspector(Object3D obj)
    {
        _primitivePanel.IsVisible = true;
        _primitiveSegmentsABox.IsVisible = true;
        _primitiveSegmentsBBox.IsVisible = true;

        switch (obj)
        {
            case Rectangle3D rect:
                _primitiveTypeText.Text = "Rectangle/Box: width, height, depth";
                _primitiveABox.Text = FormatFloat(rect.Width);
                _primitiveBBox.Text = FormatFloat(rect.Height);
                _primitiveCBox.Text = FormatFloat(rect.Depth);
                _primitiveSegmentsABox.Text = string.Empty;
                _primitiveSegmentsBBox.Text = string.Empty;
                _primitiveSegmentsABox.IsVisible = false;
                _primitiveSegmentsBBox.IsVisible = false;
                break;
            case Sphere3D sphere:
                _primitiveTypeText.Text = "Sphere: radius, segments, rings";
                _primitiveABox.Text = FormatFloat(sphere.Radius);
                _primitiveBBox.Text = string.Empty;
                _primitiveCBox.Text = string.Empty;
                _primitiveSegmentsABox.Text = sphere.Segments.ToString(CultureInfo.InvariantCulture);
                _primitiveSegmentsBBox.Text = sphere.Rings.ToString(CultureInfo.InvariantCulture);
                break;
            case Cylinder3D cylinder:
                _primitiveTypeText.Text = "Cylinder: radius, height, segments";
                _primitiveABox.Text = FormatFloat(cylinder.Radius);
                _primitiveBBox.Text = FormatFloat(cylinder.Height);
                _primitiveCBox.Text = string.Empty;
                _primitiveSegmentsABox.Text = cylinder.Segments.ToString(CultureInfo.InvariantCulture);
                _primitiveSegmentsBBox.Text = string.Empty;
                _primitiveSegmentsBBox.IsVisible = false;
                break;
            case Cone3D cone:
                _primitiveTypeText.Text = "Cone: radius, height, segments";
                _primitiveABox.Text = FormatFloat(cone.Radius);
                _primitiveBBox.Text = FormatFloat(cone.Height);
                _primitiveCBox.Text = string.Empty;
                _primitiveSegmentsABox.Text = cone.Segments.ToString(CultureInfo.InvariantCulture);
                _primitiveSegmentsBBox.Text = string.Empty;
                _primitiveSegmentsBBox.IsVisible = false;
                break;
            case Plane3D plane:
                _primitiveTypeText.Text = "Plane: width, height, segmentsX, segmentsY";
                _primitiveABox.Text = FormatFloat(plane.Width);
                _primitiveBBox.Text = FormatFloat(plane.Height);
                _primitiveCBox.Text = string.Empty;
                _primitiveSegmentsABox.Text = plane.SegmentsX.ToString(CultureInfo.InvariantCulture);
                _primitiveSegmentsBBox.Text = plane.SegmentsY.ToString(CultureInfo.InvariantCulture);
                break;
            case Ellipse3D ellipse:
                _primitiveTypeText.Text = "Ellipse: width, height, depth, segments";
                _primitiveABox.Text = FormatFloat(ellipse.Width);
                _primitiveBBox.Text = FormatFloat(ellipse.Height);
                _primitiveCBox.Text = FormatFloat(ellipse.Depth);
                _primitiveSegmentsABox.Text = ellipse.Segments.ToString(CultureInfo.InvariantCulture);
                _primitiveSegmentsBBox.Text = string.Empty;
                _primitiveSegmentsBBox.IsVisible = false;
                break;
            default:
                _primitivePanel.IsVisible = false;
                break;
        }
    }

    private void TryApplyPrimitiveValues(Object3D obj)
    {
        if (!_primitivePanel.IsVisible)
        {
            return;
        }

        switch (obj)
        {
            case Rectangle3D rect:
                if (TryReadFloat(_primitiveABox.Text, out var width)) rect.Width = width;
                if (TryReadFloat(_primitiveBBox.Text, out var height)) rect.Height = height;
                if (TryReadFloat(_primitiveCBox.Text, out var depth)) rect.Depth = depth;
                break;
            case Sphere3D sphere:
                if (TryReadFloat(_primitiveABox.Text, out var radius)) sphere.Radius = radius;
                if (TryReadInt(_primitiveSegmentsABox.Text, out var segments)) sphere.Segments = segments;
                if (TryReadInt(_primitiveSegmentsBBox.Text, out var rings)) sphere.Rings = rings;
                break;
            case Cylinder3D cylinder:
                if (TryReadFloat(_primitiveABox.Text, out var cylinderRadius)) cylinder.Radius = cylinderRadius;
                if (TryReadFloat(_primitiveBBox.Text, out var cylinderHeight)) cylinder.Height = cylinderHeight;
                if (TryReadInt(_primitiveSegmentsABox.Text, out var cylinderSegments)) cylinder.Segments = cylinderSegments;
                break;
            case Cone3D cone:
                if (TryReadFloat(_primitiveABox.Text, out var coneRadius)) cone.Radius = coneRadius;
                if (TryReadFloat(_primitiveBBox.Text, out var coneHeight)) cone.Height = coneHeight;
                if (TryReadInt(_primitiveSegmentsABox.Text, out var coneSegments)) cone.Segments = coneSegments;
                break;
            case Plane3D plane:
                if (TryReadFloat(_primitiveABox.Text, out var planeWidth)) plane.Width = planeWidth;
                if (TryReadFloat(_primitiveBBox.Text, out var planeHeight)) plane.Height = planeHeight;
                if (TryReadInt(_primitiveSegmentsABox.Text, out var sx)) plane.SegmentsX = sx;
                if (TryReadInt(_primitiveSegmentsBBox.Text, out var sy)) plane.SegmentsY = sy;
                break;
            case Ellipse3D ellipse:
                if (TryReadFloat(_primitiveABox.Text, out var ellipseWidth)) ellipse.Width = ellipseWidth;
                if (TryReadFloat(_primitiveBBox.Text, out var ellipseHeight)) ellipse.Height = ellipseHeight;
                if (TryReadFloat(_primitiveCBox.Text, out var ellipseDepth)) ellipse.Depth = ellipseDepth;
                if (TryReadInt(_primitiveSegmentsABox.Text, out var ellipseSegments)) ellipse.Segments = ellipseSegments;
                break;
        }
    }


    private void OnDebugVisualModeChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _showOnlyWorkbenchCheckBox))
        {
            var preferred = _keepSelectionPinnedCheckBox.IsChecked == true ? _selectedObject : null;
            RebuildObjectList(preferred);
        }

        ApplyDebugVisualModes();
    }

    private void SoloSelectedObject()
    {
        if (_selectedObject is null)
        {
            SetStatus("Select an object before using Solo.", isError: true);
            return;
        }

        _hideUnselectedCheckBox.IsChecked = true;
        _ghostUnselectedCheckBox.IsChecked = false;
        ApplyDebugVisualModes();
        SetStatus("Solo mode enabled. Use Reset view modes to restore the scene.", isError: false);
    }

    private void ResetDebugVisualModes()
    {
        _ghostUnselectedCheckBox.IsChecked = false;
        _hideUnselectedCheckBox.IsChecked = false;
        _showOnlyWorkbenchCheckBox.IsChecked = false;
        RestoreDebugVisuals();
        RebuildObjectList(_selectedObject);
        SetStatus("Debug visual modes reset.", isError: false);
    }

    private void ApplyDebugVisualModes()
    {
        if (_debugVisualsApplying)
        {
            return;
        }

        var ghost = _ghostUnselectedCheckBox.IsChecked == true;
        var hide = _hideUnselectedCheckBox.IsChecked == true;
        if (!ghost && !hide)
        {
            RestoreDebugVisuals();
            return;
        }

        _debugVisualsApplying = true;
        try
        {
            foreach (var obj in EnumerateObjects(_viewport.Scene))
            {
                SaveDebugVisualState(obj);
                var keepNormal = IsSelectionFocusObject(obj);
                if (keepNormal)
                {
                    RestoreDebugVisualState(obj);
                    continue;
                }

                if (hide)
                {
                    obj.IsVisible = false;
                    continue;
                }

                obj.IsVisible = true;
                var baseColor = obj.Material.BaseColor;
                obj.Material.BaseColor = new ColorRgba(baseColor.R, baseColor.G, baseColor.B, MathF.Min(baseColor.A, 0.18f));
                obj.Material.Opacity = MathF.Min(obj.Material.Opacity, 0.18f);
            }
        }
        finally
        {
            _debugVisualsApplying = false;
        }
    }

    private void RestoreDebugVisuals()
    {
        if (_debugVisualStates.Count == 0)
        {
            return;
        }

        _debugVisualsApplying = true;
        try
        {
            foreach (var obj in EnumerateObjects(_viewport.Scene))
            {
                RestoreDebugVisualState(obj);
            }

            _debugVisualStates.Clear();
        }
        finally
        {
            _debugVisualsApplying = false;
        }
    }

    private void SaveDebugVisualState(Object3D obj)
    {
        if (_debugVisualStates.ContainsKey(obj.Id))
        {
            return;
        }

        _debugVisualStates[obj.Id] = new DebugVisualState(obj.IsVisible, obj.Material.BaseColor, obj.Material.Opacity);
    }

    private void RestoreDebugVisualState(Object3D obj)
    {
        if (!_debugVisualStates.TryGetValue(obj.Id, out var state))
        {
            return;
        }

        obj.IsVisible = state.IsVisible;
        obj.Material.BaseColor = state.BaseColor;
        obj.Material.Opacity = state.Opacity;
    }

    private bool IsSelectionFocusObject(Object3D obj)
    {
        var selected = _selectedObject;
        if (selected is null)
        {
            return false;
        }

        if (ReferenceEquals(obj, selected))
        {
            return true;
        }

        for (var parent = obj.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, selected))
            {
                return true;
            }
        }

        for (var parent = selected.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, obj))
            {
                return true;
            }
        }

        return false;
    }

    private async System.Threading.Tasks.Task CopySceneSnippetAsync()
    {
        var text = BuildSceneWorkbenchSnippet();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
            SetStatus("Scene/workbench snippet copied.", isError: false);
        }
    }

    private async System.Threading.Tasks.Task CopyDiagnosticsAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(BuildDiagnosticsText());
        SetStatus("Diagnostics copied.", isError: false);
    }

    private void UpdateSpaceGuides()
    {
        if (_spaceGuidesUpdating)
        {
            return;
        }

        _spaceGuidesUpdating = true;
        try
        {
            RemoveSpaceGuides();
            if (_showBasisCheckBox.IsChecked == true)
            {
                AddBasisAxes();
            }

            if (_showGroundGridCheckBox.IsChecked == true)
            {
                AddGroundGrid();
            }

            if (_showLightGizmosCheckBox.IsChecked == true)
            {
                AddLightGizmos();
            }

            UpdateSpaceReadout(null, null);
        }
        finally
        {
            _spaceGuidesUpdating = false;
        }
    }

    private void RemoveSpaceGuides()
    {
        if (_spaceGuideObjects.Count == 0)
        {
            return;
        }

        foreach (var obj in _spaceGuideObjects.ToArray())
        {
            _viewport.Scene.Remove(obj);
        }

        _spaceGuideObjects.Clear();
    }

    private void AddBasisAxes()
    {
        AddSpaceGuide(new Box3D { Name = "Debug basis X red", Width = 4f, Height = 0.025f, Depth = 0.025f, Position = new Vector3(2f, 0f, 0f), IsPickable = false, IsManipulationEnabled = false, Material = Material3D.CreateUnlit(new ColorRgba(1f, 0.08f, 0.06f, 0.92f)) });
        AddSpaceGuide(new Box3D { Name = "Debug basis Y green", Width = 0.025f, Height = 4f, Depth = 0.025f, Position = new Vector3(0f, 2f, 0f), IsPickable = false, IsManipulationEnabled = false, Material = Material3D.CreateUnlit(new ColorRgba(0.1f, 0.95f, 0.18f, 0.92f)) });
        AddSpaceGuide(new Box3D { Name = "Debug basis Z blue", Width = 0.025f, Height = 0.025f, Depth = 4f, Position = new Vector3(0f, 0f, 2f), IsPickable = false, IsManipulationEnabled = false, Material = Material3D.CreateUnlit(new ColorRgba(0.15f, 0.35f, 1f, 0.92f)) });
        AddSpaceGuide(new Sphere3D { Name = "Debug origin", Radius = 0.06f, Segments = 16, Rings = 8, IsPickable = false, IsManipulationEnabled = false, Material = Material3D.CreateUnlit(new ColorRgba(1f, 1f, 1f, 0.95f)) });
    }

    private void AddGroundGrid()
    {
        const int half = 10;
        const float extent = half * 1f;
        var major = Material3D.CreateUnlit(new ColorRgba(0.55f, 0.58f, 0.63f, 0.22f));
        var minor = Material3D.CreateUnlit(new ColorRgba(0.42f, 0.45f, 0.5f, 0.13f));
        for (var i = -half; i <= half; i++)
        {
            var material = i == 0 ? major : minor;
            var thickness = i == 0 ? 0.018f : 0.008f;
            AddSpaceGuide(new Box3D { Name = "Debug grid X " + i.ToString(CultureInfo.InvariantCulture), Width = extent * 2f, Height = 0.004f, Depth = thickness, Position = new Vector3(0f, -0.002f, i), IsPickable = false, IsManipulationEnabled = false, Material = material });
            AddSpaceGuide(new Box3D { Name = "Debug grid Z " + i.ToString(CultureInfo.InvariantCulture), Width = thickness, Height = 0.004f, Depth = extent * 2f, Position = new Vector3(i, -0.003f, 0f), IsPickable = false, IsManipulationEnabled = false, Material = material });
        }
    }

    private void AddLightGizmos()
    {
        var directional = _viewport.Scene.Lights.FirstOrDefault();
        if (directional is { IsEnabled: true })
        {
            var direction = directional.Direction.LengthSquared() > 0.000001f ? Vector3.Normalize(directional.Direction) : Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f));
            var start = -direction * 2.6f + new Vector3(0f, 2f, 0f);
            var length = 2.2f;
            var material = Material3D.CreateUnlit(new ColorRgba(1f, 0.92f, 0.18f, 0.85f));
            AddSpaceGuide(new Sphere3D
            {
                Name = "Debug directional light source",
                Radius = 0.12f,
                Segments = 16,
                Rings = 8,
                Position = start,
                IsPickable = false,
                IsManipulationEnabled = false,
                Material = material
            });
            AddSpaceGuide(new Box3D
            {
                Name = "Debug directional light direction",
                Width = 0.045f,
                Height = 0.045f,
                Depth = length,
                Position = start + direction * (length * 0.5f),
                RotationDegrees = DirectionToEulerDegrees(direction),
                IsPickable = false,
                IsManipulationEnabled = false,
                Material = material
            });
        }

        var point = _viewport.Scene.PointLights.FirstOrDefault();
        if (point is { IsEnabled: true })
        {
            var material = Material3D.CreateUnlit(new ColorRgba(1f, 0.48f, 0.12f, 0.82f));
            AddSpaceGuide(new Sphere3D
            {
                Name = "Debug point light",
                Radius = 0.16f,
                Segments = 20,
                Rings = 10,
                Position = point.Position,
                IsPickable = false,
                IsManipulationEnabled = false,
                Material = material
            });
            AddSpaceGuide(new Sphere3D
            {
                Name = "Debug point light range",
                Radius = MathF.Max(0.05f, point.Range),
                Segments = 32,
                Rings = 16,
                Position = point.Position,
                IsPickable = false,
                IsManipulationEnabled = false,
                Material = Material3D.CreateUnlit(new ColorRgba(1f, 0.48f, 0.12f, 0.08f))
            });
        }
    }

    private static Vector3 DirectionToEulerDegrees(Vector3 direction)
    {
        direction = direction.LengthSquared() > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitZ;
        var yaw = MathF.Atan2(direction.X, direction.Z) * 180f / MathF.PI;
        var horizontal = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        var pitch = -MathF.Atan2(direction.Y, horizontal) * 180f / MathF.PI;
        return new Vector3(pitch, yaw, 0f);
    }

    private void AddSpaceGuide(Object3D obj)
    {
        obj.DataContext = DebugGuideTag.Instance;
        _spaceGuideObjects.Add(obj);
        _viewport.Scene.Add(obj);
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(_viewport);
        Vector3? ground = null;
        if (_viewport.Bounds.Width > 0 && _viewport.Bounds.Height > 0)
        {
            var ray = ProjectionHelper3D.CreateRay(
                _viewport.Scene.Camera,
                new Vector2((float)p.X, (float)p.Y),
                new Vector2((float)_viewport.Bounds.Width, (float)_viewport.Bounds.Height));
            if (MathF.Abs(ray.Direction.Y) > 0.00001f)
            {
                var t = -ray.Origin.Y / ray.Direction.Y;
                if (t >= 0f)
                {
                    ground = ray.Origin + ray.Direction * t;
                }
            }
        }

        UpdateSpaceReadout(p, ground);
    }

    private void UpdateSpaceReadout(Point? pointer, Vector3? ground)
    {
        var camera = _viewport.Scene.Camera;
        var pointerText = pointer is null ? "--" : $"{pointer.Value.X:0}, {pointer.Value.Y:0}";
        var groundText = ground is null ? "--" : FormatVector(ground.Value);
        _spaceReadoutText.Text =
            "Basis: X red, Y green, Z blue\n" +
            "Camera: " + FormatVector(camera.Position) + " -> " + FormatVector(camera.Target) + "\n" +
            "Pointer: " + pointerText + " | Ground Y=0: " + groundText;
    }

    private void FocusSelectedObject()
    {
        var obj = _selectedObject;
        if (obj is null)
        {
            return;
        }

        var bounds = obj.WorldBounds;
        if (!bounds.IsValid)
        {
            SetStatus("Selected object has no valid bounds to frame.", isError: true);
            return;
        }

        var center = bounds.Center;
        var extents = bounds.Size * 0.5f;
        var radius = MathF.Max(0.4f, MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z)) * 2.8f);
        _viewport.Scene.Camera.Target = center;
        _viewport.Scene.Camera.Position = center + new Vector3(radius, radius * 0.75f, -radius * 1.45f);
        SetStatus("Camera framed selected object.", isError: false);
    }

    private readonly record struct DebugVisualState(bool IsVisible, ColorRgba BaseColor, float Opacity);

    private sealed class DebugWorkbenchTag
    {
        public static readonly DebugWorkbenchTag Instance = new();
        private DebugWorkbenchTag() { }
    }

    private sealed class DebugGuideTag
    {
        public static readonly DebugGuideTag Instance = new();
        private DebugGuideTag() { }
    }

    private sealed class SourceTargetInfo
    {
        public SourceTargetInfo(string filePath, string className, int classStart, int classEnd, int classOpenBrace, int classCloseBrace, string indent, int line, int buildMethodStart, int buildMethodEnd, string? buildParameterName)
        {
            FilePath = filePath;
            ClassName = className;
            ClassStart = classStart;
            ClassEnd = classEnd;
            ClassOpenBrace = classOpenBrace;
            ClassCloseBrace = classCloseBrace;
            Indent = indent;
            Line = line;
            BuildMethodStart = buildMethodStart;
            BuildMethodEnd = buildMethodEnd;
            BuildParameterName = buildParameterName;
        }

        public string FilePath { get; }
        public string ClassName { get; }
        public int ClassStart { get; }
        public int ClassEnd { get; }
        public int ClassOpenBrace { get; }
        public int ClassCloseBrace { get; }
        public string Indent { get; }
        public int Line { get; }
        public int BuildMethodStart { get; }
        public int BuildMethodEnd { get; }
        public string? BuildParameterName { get; }
        public bool HasBuildMethod => BuildMethodStart >= 0 && BuildMethodEnd >= BuildMethodStart;
    }

    private sealed class PreviewObjectEntry
    {
        public PreviewObjectEntry(Object3D obj, int depth, string path)
        {
            Object = obj;
            Depth = depth;
            Path = path;
        }

        public Object3D Object { get; }
        public int Depth { get; }
        public string Path { get; }

        public override string ToString()
        {
            var indent = new string(' ', Depth * 2);
            var marker = Object is CompositeObject3D ? "▸" : "•";
            return $"{indent}{marker} {Object.Name} [{Object.GetType().Name}]";
        }
    }
}
