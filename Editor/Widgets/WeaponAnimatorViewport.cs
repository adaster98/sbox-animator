#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

internal readonly record struct GridVisualStyle(
	float MinorOpacity,
	float MajorOpacity,
	float AxisOpacity,
	float MinorWidth,
	float MajorWidth,
	float AxisWidth )
{
	public static GridVisualStyle Resolve( float opacity, float lineWeight )
	{
		var alpha = Math.Clamp( opacity, 0, 0.5f );
		var weight = Math.Clamp( lineWeight, 0.1f, 2.0f );
		return new GridVisualStyle(
			alpha * 0.42f,
			alpha * 0.70f,
			alpha,
			weight * 0.38f,
			weight * 0.58f,
			weight * 0.78f );
	}
}

public enum WeaponAnimatorTransformMode
{
	Move,
	Rotate,
	Scale
}

public sealed class WeaponAnimatorViewport : SceneRenderingWidget
{
	private const int LegacyIdleRepairVersion = 3;
	private const float ScaleGizmoSensitivity = 0.005f;
	private static readonly Rect TransformReadoutRect =
		new( 150, 10, 104, 28 );
	private readonly WeaponAnimatorController _controller;
	private readonly CameraComponent _camera;
	private readonly WeaponAnimatorButton _moveModeButton;
	private readonly WeaponAnimatorButton _rotateModeButton;
	private readonly WeaponAnimatorButton _scaleModeButton;
	private readonly WeaponAnimatorButton _spaceButton;
	private readonly WeaponAnimatorButton _orbitCameraButton;
	private readonly WeaponAnimatorButton _freeLookCameraButton;
	private readonly WeaponAnimatorButton _lightingButton;
	private string _transformModeText = "";
	private SkinnedModelRenderer? _sourceRenderer;
	private SkinnedModelRenderer? _armsRenderer;
	private SkinnedModelRenderer? _hostRenderer;
	private HostSkeleton? _hostSkeleton;
	private string _loadedSource = "";
	private string _loadedHost = "";
	private string _lastDiagnosticSelection = "";
	private int _legacyIdleRepairVersionChecked;
	private bool _sourcePoseDiagnosticsLogged;
	private int _sourcePoseDiagnosticFrames;
	private bool _armPoseDiagnosticsLogged;
	private int _armPoseDiagnosticFrames;
	private Vector2 _lastMouse;
	private readonly Dictionary<string, Transform> _testPose = new( StringComparer.OrdinalIgnoreCase );
	private float _playbackTime;
	private string _animationGizmoTarget = "";
	private RigControlKind _animationGizmoKind;
	private Transform _animationGizmoStartLocal;
	private Transform _animationGizmoStartWorld;
	private Transform? _animationGizmoStartParent;
	private Vector3 _animationGizmoMoveDelta;
	private Vector3 _animationGizmoScaleDelta;
	private RealTimeSince _sinceCameraSpeedChanged = 99;

	public ViewportPickMode PickMode { get; set; }
	public bool IsPlaying { get; private set; }
	public WeaponAnimatorTransformMode TransformMode { get; private set; }
	public Vector3 ModelDimensions => _sourceRenderer?.Model?.Bounds.Size ?? Vector3.Zero;
	public event Action<string>? StatusChanged;
	public event Action<Vector3>? ModelDimensionsChanged;
	public event Action? LegacyIdleRepaired;

	public WeaponAnimatorViewport(
		WeaponAnimatorController controller,
		Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		MinimumSize = new Vector2( 420, 280 );
		FocusMode = FocusMode.Click;
		MouseTracking = true;
		Scene = Scene.CreateEditorScene();

		using ( Scene.Push() )
		{
			_camera = new GameObject( true, "weapon_animator_camera" )
				.GetOrAddComponent<CameraComponent>( false );
			_camera.BackgroundColor = WeaponAnimatorTheme.Background;
			_camera.ZNear = 0.5f;
			_camera.ZFar = 8192;
			_camera.Enabled = true;
			Camera = _camera;

			var ambient = new GameObject( true, "ambient" )
				.GetOrAddComponent<AmbientLight>( false );
			ambient.Color = new Color( 0.26f, 0.29f, 0.33f );
			ambient.Enabled = true;

			var key = new GameObject( true, "key_light" )
				.GetOrAddComponent<DirectionalLight>( false );
			key.WorldRotation = Rotation.From( 38, 135, 0 );
			key.LightColor = new Color( 1.0f, 0.92f, 0.82f ) * 1.3f;
			key.SkyColor = new Color( 0.18f, 0.22f, 0.27f );
			key.Enabled = true;

			var rim = new GameObject( true, "rim_light" )
				.GetOrAddComponent<PointLight>( false );
			rim.WorldPosition = new Vector3( -32, 38, 28 );
			rim.LightColor = WeaponAnimatorTheme.Cyan * 12;
			rim.Radius = 160;
			rim.Enabled = true;
		}

		_moveModeButton = AddTransformModeButton(
			"open_with",
			"Move (W)",
			WeaponAnimatorTransformMode.Move,
			new Vector2( 10, 10 ) );
		_rotateModeButton = AddTransformModeButton(
			"360",
			"Rotate (E)",
			WeaponAnimatorTransformMode.Rotate,
			new Vector2( 41, 10 ) );
		_scaleModeButton = AddTransformModeButton(
			"zoom_out_map",
			"Scale (R)",
			WeaponAnimatorTransformMode.Scale,
			new Vector2( 72, 10 ) );
		_spaceButton = new WeaponAnimatorButton( "", "public", this )
		{
			IsToggle = true,
			Clicked = ToggleTransformSpace,
			Position = new Vector2( 119, 10 ),
			FixedWidth = 28,
			FixedHeight = 28
		};
		_spaceButton.Raise();
		_orbitCameraButton = AddViewportActionButton(
			"360",
			"Orbit camera",
			() => SetCameraMode( false ) );
		_freeLookCameraButton = AddViewportActionButton(
			"videocam",
			"Free look camera — RMB look, WASD move, wheel changes speed, Shift moves faster",
			() => SetCameraMode( true ) );
		_lightingButton = AddViewportActionButton(
			"light_mode",
			"Toggle lit / full bright",
			ToggleViewportLighting );
		PositionViewportActions();

		_controller.DocumentChanged += OnDocumentChanged;
		_controller.PoseChanged += Update;
		_controller.SelectionChanged += OnSelectionChanged;
		_controller.TimelineChanged += Update;
		RefreshTransformOverlay();
		RefreshViewportCameraButtons();
		RebuildPreview();
	}

	protected override void OnResize()
	{
		base.OnResize();
		PositionViewportActions();
	}

	public override void OnDestroyed()
	{
		EndAnimationGizmoDrag();
		_controller.DocumentChanged -= OnDocumentChanged;
		_controller.PoseChanged -= Update;
		_controller.SelectionChanged -= OnSelectionChanged;
		_controller.TimelineChanged -= Update;
		Scene?.Destroy();
		Scene = null;
		base.OnDestroyed();
	}

	public void TogglePlayback()
	{
		IsPlaying = !IsPlaying;
		_playbackTime = _controller.Document.Workspace.TimelineTime;
	}

	public void StopPlayback()
	{
		IsPlaying = false;
	}

	public void SetTransformMode( WeaponAnimatorTransformMode mode )
	{
		if ( TransformMode == mode )
		{
			RefreshTransformOverlay();
			return;
		}

		EndAnimationGizmoDrag();
		TransformMode = mode;
		RefreshTransformOverlay();
		StatusChanged?.Invoke( $"{TransformModeName( mode )} gizmo selected." );
		Update();
	}

	private WeaponAnimatorButton AddTransformModeButton(
		string icon,
		string tooltip,
		WeaponAnimatorTransformMode mode,
		Vector2 position )
	{
		var button = new WeaponAnimatorButton( "", icon, this )
		{
			IsToggle = true,
			Clicked = () => SetTransformMode( mode ),
			Position = position,
			FixedWidth = 28,
			FixedHeight = 28,
			ToolTip = tooltip
		};
		button.Raise();
		return button;
	}

	private WeaponAnimatorButton AddViewportActionButton(
		string icon,
		string tooltip,
		Action clicked )
	{
		var button = new WeaponAnimatorButton( "", icon, this )
		{
			IsToggle = true,
			Clicked = clicked,
			FixedWidth = 28,
			FixedHeight = 28,
			ToolTip = tooltip
		};
		button.Raise();
		return button;
	}

	private void PositionViewportActions()
	{
		if ( _lightingButton is null )
			return;

		var right = MathF.Max( Width - 10, 113 );
		_lightingButton.Position = new Vector2( right - 28, 10 );
		_freeLookCameraButton.Position = new Vector2( right - 72, 10 );
		_orbitCameraButton.Position = new Vector2( right - 103, 10 );
	}

	private void SetCameraMode( bool freeLook )
	{
		var workspace = _controller.Document.Workspace;
		if ( workspace.FreeLookCamera == freeLook )
		{
			RefreshViewportCameraButtons();
			return;
		}

		_controller.UpdateWorkspacePreference(
			freeLook ? "Free look camera" : "Orbit camera",
			state =>
			{
				state.FirstPersonPreview = false;
				if ( freeLook )
				{
					state.CameraPosition = _camera.WorldPosition;
				}
				else
				{
					var rotation = Rotation.From( state.CameraAngles );
					state.CameraFocus = state.CameraPosition
						+ rotation.Forward * state.CameraDistance;
				}
				state.FreeLookCamera = freeLook;
			} );
		RefreshViewportCameraButtons();
		UpdateCamera();
	}

	private void ToggleViewportLighting()
	{
		_controller.UpdateWorkspacePreference(
			"Viewport lighting",
			state => state.FullBrightViewport = !state.FullBrightViewport );
		RefreshViewportCameraButtons();
		UpdateCamera();
	}

	private void RefreshViewportCameraButtons()
	{
		var workspace = _controller.Document.Workspace;
		RefreshTransformModeButton(
			_orbitCameraButton,
			!workspace.FreeLookCamera );
		RefreshTransformModeButton(
			_freeLookCameraButton,
			workspace.FreeLookCamera );
		_lightingButton.IsChecked = workspace.FullBrightViewport;
		_lightingButton.Tint = workspace.FullBrightViewport
			? WeaponAnimatorTheme.Amber * 0.55f
			: WeaponAnimatorTheme.SurfaceRaised;
		_lightingButton.ToolTip = workspace.FullBrightViewport
			? "Full bright — click for Lit"
			: "Lit — click for Full bright";
	}

	private void ToggleTransformSpace()
	{
		_controller.Mutate(
			"Transform coordinate space",
			document => document.Workspace.LocalGizmos =
				!document.Workspace.LocalGizmos );
		RefreshTransformOverlay();
	}

	private void RefreshTransformOverlay()
	{
		var local = _controller.Document.Workspace.LocalGizmos;
		RefreshTransformModeButton(
			_moveModeButton,
			TransformMode == WeaponAnimatorTransformMode.Move );
		RefreshTransformModeButton(
			_rotateModeButton,
			TransformMode == WeaponAnimatorTransformMode.Rotate );
		RefreshTransformModeButton(
			_scaleModeButton,
			TransformMode == WeaponAnimatorTransformMode.Scale );
		_spaceButton.IsChecked = !local;
		_spaceButton.Tint = local
			? WeaponAnimatorTheme.SurfaceRaised
			: WeaponAnimatorTheme.Cyan * 0.55f;
		_spaceButton.ToolTip = local
			? "Local space — click for World"
			: "World space — click for Local";
		_transformModeText =
			$"{TransformModeName( TransformMode ).ToUpperInvariant()} · {(local ? "LOCAL" : "WORLD")}";
	}

	private static void RefreshTransformModeButton(
		WeaponAnimatorButton button,
		bool selected )
	{
		button.IsChecked = selected;
		button.Tint = selected
			? WeaponAnimatorTheme.Cyan * 0.55f
			: WeaponAnimatorTheme.SurfaceRaised;
	}

	private static string TransformModeName( WeaponAnimatorTransformMode mode ) =>
		mode switch
		{
			WeaponAnimatorTransformMode.Rotate => "Rotate",
			WeaponAnimatorTransformMode.Scale => "Scale",
			_ => "Move"
		};

	public void ResetTestPose()
	{
		_testPose.Clear();
		_sourceRenderer?.ClearPhysicsBones();
		StatusChanged?.Invoke( "Temporary movement test pose reset." );
	}

	public void SetPickMode( ViewportPickMode mode )
	{
		PickMode = mode;
		StatusChanged?.Invoke( mode == ViewportPickMode.None
			? "Pick mode cleared."
			: $"Click the weapon surface or a bone to set {PickLabel( mode )}." );
	}

	public void FitCamera()
	{
		var bounds = _sourceRenderer?.Bounds ?? _hostRenderer?.Bounds;
		if ( bounds is null )
			return;

		var workspace = _controller.Document.Workspace;
		workspace.CameraFocus = bounds.Value.Center;
		workspace.CameraDistance =
			MathF.Max( bounds.Value.Size.Length * 1.25f, 12 );
		if ( workspace.FreeLookCamera )
		{
			var rotation = Rotation.From( workspace.CameraAngles );
			workspace.CameraPosition = workspace.CameraFocus
				- rotation.Forward * workspace.CameraDistance;
		}
		UpdateCamera();
	}

	public void RebuildPreview()
	{
		if ( !Scene.IsValid() )
			return;

		using ( Scene.Push() )
		{
			_sourceRenderer?.GameObject.Destroy();
			_armsRenderer?.GameObject.Destroy();
			_hostRenderer?.GameObject.Destroy();
			_sourceRenderer = null;
			_armsRenderer = null;
			_hostRenderer = null;
			_hostSkeleton = null;
			_loadedSource = "";
			_loadedHost = "";
			_sourcePoseDiagnosticsLogged = false;
			_sourcePoseDiagnosticFrames = 0;
			_armPoseDiagnosticsLogged = false;
			_armPoseDiagnosticFrames = 0;

			var document = _controller.Document;
			if ( !string.IsNullOrWhiteSpace( document.Source.CompiledModelPath ) )
			{
				var sourceModel = Model.Load( document.Source.CompiledModelPath );
				if ( sourceModel is not null && !sourceModel.IsError )
				{
					_sourceRenderer = new GameObject( true, "source_weapon_preview" )
						.GetOrAddComponent<SkinnedModelRenderer>( false );
					_sourceRenderer.Model = sourceModel;
					_sourceRenderer.Enabled = true;
					_loadedSource = document.Source.CompiledModelPath;
					ModelDimensionsChanged?.Invoke( sourceModel.Bounds.Size );
				}
			}

			var armsModel = Model.Load( HostSkeletonBuilder.ProductionArmsModel );
			if ( armsModel is null || armsModel.IsError )
				armsModel = HostSkeletonBuilder.LoadArmProfile();
			if ( armsModel is not null && !armsModel.IsError )
			{
				_armsRenderer = new GameObject( true, "facepunch_arms_preview" )
					.GetOrAddComponent<SkinnedModelRenderer>( false );
				_armsRenderer.Model = armsModel;
				_armsRenderer.Enabled = true;
				_armsRenderer.Tint = new Color( 0.42f, 0.84f, 0.92f, 0.42f );
			}

			if ( document.ActiveStage == WeaponAnimatorStage.Animate
				&& !string.IsNullOrWhiteSpace( document.Source.PreviewHostPath ) )
			{
				var hostModel = Model.Load( document.Source.PreviewHostPath );
				if ( hostModel is not null && !hostModel.IsError )
				{
					_hostRenderer = new GameObject( true, "animation_host_preview" )
						.GetOrAddComponent<SkinnedModelRenderer>( false );
					_hostRenderer.Model = hostModel;
					_hostRenderer.Enabled = true;
					_hostRenderer.UseAnimGraph = false;
					SuppressHostRendering();
					_hostSkeleton = HostSkeletonBuilder.Build( document );
					_loadedHost = document.Source.PreviewHostPath;

					if ( _sourceRenderer.IsValid() )
					{
						_sourceRenderer!.WorldTransform = Transform.Zero;
						_sourceRenderer.BoneMergeTarget = null;
					}
					if ( _armsRenderer.IsValid() )
					{
						_armsRenderer!.WorldTransform = Transform.Zero;
						_armsRenderer.BoneMergeTarget = null;
						_armsRenderer.Tint = Color.White;
					}
				}
			}
		}

		if ( _controller.Document.Workspace.CameraDistance <= 0 )
			FitCamera();
		Update();
	}

	protected override void PreFrame()
	{
		Scene.EditorTick( RealTime.Now, RealTime.Delta );
		GizmoInstance.Input.IsHovered = IsActiveWindow && IsUnderMouse;
		UpdateGizmoInputs( GizmoInstance.Input.IsHovered );
		FinishAnimationGizmoDragIfReleased();

		if ( RepairLegacyIdleIfNeeded() )
			return;
		EnsurePreviewCurrent();
		AdvancePlayback();
		UpdateFreeLookMovement();
		UpdateCamera();

		DrawWorkspaceGrid();
		if ( _controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate )
			DrawCalibration();
		else
			DrawAnimation();

		DrawScreenGuides();
		DrawViewportToolReadout();
		DrawCameraSpeedOverlay();
		Cursor = Gizmo.HasHovered || PickMode != ViewportPickMode.None
			? CursorShape.Finger
			: _controller.Document.Workspace.FreeLookCamera
				&& global::Editor.Application.MouseButtons.HasFlag( MouseButtons.Right )
				&& IsUnderMouse
					? CursorShape.Blank
					: CursorShape.Arrow;
	}

	private void DrawWorkspaceGrid()
	{
		var style = GridVisualStyle.Resolve(
			_controller.Document.Workspace.GridOpacity,
			_controller.Document.Workspace.GridLineThickness );
		if ( style.AxisOpacity <= 0 )
			return;

		var spacing = MathF.Max( Gizmo.Settings.GridSpacing, 1 );
		var desiredExtent = MathF.Max(
			128,
			_controller.Document.Workspace.CameraDistance * 6 );
		var halfLines = Math.Clamp(
			(int)MathF.Ceiling( desiredExtent / spacing ),
			8,
			64 );
		var extent = halfLines * spacing;

		using var scope = Gizmo.Scope( "weapon_animator_grid" );
		for ( var index = -halfLines; index <= halfLines; index++ )
		{
			if ( index == 0 )
				continue;

			var coordinate = index * spacing;
			var major = index % 4 == 0;
			Gizmo.Draw.Color = Color.White.WithAlpha(
				major ? style.MajorOpacity : style.MinorOpacity );
			Gizmo.Draw.LineThickness = major ? style.MajorWidth : style.MinorWidth;
			Gizmo.Draw.Line(
				new Vector3( coordinate, -extent, 0 ),
				new Vector3( coordinate, extent, 0 ) );
			Gizmo.Draw.Line(
				new Vector3( -extent, coordinate, 0 ),
				new Vector3( extent, coordinate, 0 ) );
		}

		Gizmo.Draw.LineThickness = style.AxisWidth;
		Gizmo.Draw.Color = new Color( 0.90f, 0.28f, 0.38f ).WithAlpha( style.AxisOpacity );
		Gizmo.Draw.Line( new Vector3( -extent, 0, 0 ), new Vector3( extent, 0, 0 ) );
		Gizmo.Draw.Color = new Color( 0.58f, 0.78f, 0.20f ).WithAlpha( style.AxisOpacity );
		Gizmo.Draw.Line( new Vector3( 0, -extent, 0 ), new Vector3( 0, extent, 0 ) );
		Gizmo.Draw.LineThickness = 1;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		var delta = e.LocalPosition - _lastMouse;
		_lastMouse = e.LocalPosition;
		if ( (e.ButtonState & MouseButtons.Right) == 0
			|| _controller.Document.Workspace.FirstPersonPreview )
			return;

		var workspace = _controller.Document.Workspace;
		workspace.CameraAngles = new Angles(
			Math.Clamp( workspace.CameraAngles.pitch + delta.y * 0.22f, -88, 88 ),
			workspace.CameraAngles.yaw - delta.x * 0.22f,
			0 );
		_controller.MarkWorkspacePreferenceChanged( "Viewport camera rotation" );
		UpdateCamera();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		_lastMouse = e.LocalPosition;
		if ( !e.LeftMouseButton || PickMode == ViewportPickMode.None )
			return;

		if ( TryPickSourceSurface( e.LocalPosition, out var localPosition ) )
		{
			ApplyPickedPoint( localPosition );
			e.Accepted = true;
		}
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		if ( _controller.Document.Workspace.FirstPersonPreview )
			return;

		var workspace = _controller.Document.Workspace;
		if ( workspace.FreeLookCamera )
		{
			var direction = Math.Sign( e.Delta );
			_controller.UpdateWorkspacePreference(
				"Free look camera speed",
				state => state.CameraMoveSpeed = AdjustCameraSpeed(
					state.CameraMoveSpeed,
					direction ) );
			_sinceCameraSpeedChanged = 0;
			e.Accept();
			Update();
			return;
		}

		workspace.CameraDistance = Math.Clamp(
			workspace.CameraDistance * (e.Delta > 0 ? 0.9f : 1.1f),
			2,
			4096 );
		_controller.MarkWorkspacePreferenceChanged( "Orbit camera distance" );
		e.Accept();
	}

	private void OnDocumentChanged()
	{
		RefreshTransformOverlay();
		RefreshViewportCameraButtons();
		var document = _controller.Document;
		if ( document.Source.CompiledModelPath != _loadedSource
			|| (document.ActiveStage == WeaponAnimatorStage.Animate
				&& document.Source.PreviewHostPath != _loadedHost)
			|| (document.ActiveStage == WeaponAnimatorStage.Calibrate && _hostRenderer.IsValid()) )
		{
			RebuildPreview();
			return;
		}

		Update();
	}

	private void EnsurePreviewCurrent()
	{
		if ( !Scene.IsValid() )
			return;
		if ( _sourceRenderer is null && !string.IsNullOrWhiteSpace( _controller.Document.Source.CompiledModelPath ) )
			RebuildPreview();
	}

	private void AdvancePlayback()
	{
		if ( !IsPlaying )
			return;

		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null || clip.Duration <= 0 )
			return;

		_playbackTime += RealTime.Delta;
		if ( _playbackTime > clip.Duration )
		{
			if ( clip.Loop )
				_playbackTime %= clip.Duration;
			else
			{
				_playbackTime = clip.Duration;
				IsPlaying = false;
			}
		}
		_controller.SetTimelineTime( _playbackTime );
	}

	private void UpdateCamera()
	{
		if ( !_camera.IsValid() )
			return;

		var document = _controller.Document;
		_camera.DebugMode = document.Workspace.FullBrightViewport
			? SceneCameraDebugMode.FullBright
			: SceneCameraDebugMode.Normal;
		if ( document.Workspace.FirstPersonPreview )
		{
			_camera.WorldPosition = Vector3.Zero;
			_camera.WorldRotation = Rotation.Identity;
			var aspect = GuideAspect( document.Calibration.AspectGuide );
			var horizontalRadians = document.Calibration.HorizontalFov.DegreeToRadian();
			_camera.FieldOfView = (2.0f * MathF.Atan(
				MathF.Tan( horizontalRadians * 0.5f ) / aspect )).RadianToDegree();
			return;
		}

		var rotation = Rotation.From( document.Workspace.CameraAngles );
		if ( document.Workspace.FreeLookCamera )
		{
			_camera.WorldPosition = document.Workspace.CameraPosition;
			_camera.WorldRotation = rotation;
			_camera.FieldOfView = 48;
			return;
		}

		var focus = document.Workspace.CameraFocus;
		_camera.WorldPosition = focus - rotation.Forward * document.Workspace.CameraDistance;
		_camera.WorldRotation = Rotation.LookAt( focus - _camera.WorldPosition, Vector3.Up );
		_camera.FieldOfView = 48;
	}

	private void UpdateFreeLookMovement()
	{
		var workspace = _controller.Document.Workspace;
		if ( !workspace.FreeLookCamera
			|| workspace.FirstPersonPreview
			|| !IsActiveWindow
			|| !IsUnderMouse
			|| PickMode != ViewportPickMode.None
			|| Gizmo.Pressed.Any )
			return;

		var rotation = Rotation.From( workspace.CameraAngles );
		var movement = Vector3.Zero;
		if ( global::Editor.Application.IsKeyDown( KeyCode.W ) )
			movement += rotation.Forward;
		if ( global::Editor.Application.IsKeyDown( KeyCode.S ) )
			movement += rotation.Backward;
		if ( global::Editor.Application.IsKeyDown( KeyCode.A ) )
			movement += rotation.Left;
		if ( global::Editor.Application.IsKeyDown( KeyCode.D ) )
			movement += rotation.Right;
		if ( movement.IsNearZeroLength )
			return;

		var fast = global::Editor.Application.KeyboardModifiers
			.HasFlag( KeyboardModifiers.Shift );
		var speed = workspace.CameraMoveSpeed * 100.0f * (fast ? 8.0f : 1.0f);
		workspace.CameraPosition += movement.Normal * speed * RealTime.Delta;
		_controller.MarkWorkspacePreferenceChanged( "Free look camera position" );
	}

	internal static float AdjustCameraSpeed( float currentSpeed, int direction )
	{
		currentSpeed = Math.Clamp( currentSpeed, 0.25f, 100.0f );
		var adjustment = currentSpeed < 5.0f
			? 0.25f
			: currentSpeed < 20.0f
				? 1.0f
				: MathF.Round( currentSpeed * 0.1f / 2.5f ) * 2.5f;
		return Math.Clamp(
			currentSpeed + adjustment * Math.Sign( direction ),
			0.25f,
			100.0f );
	}

	private void DrawCalibration()
	{
		var document = _controller.Document;
		if ( _sourceRenderer.IsValid() )
		{
			_sourceRenderer!.BoneMergeTarget = null;
			_sourceRenderer.WorldTransform = WeaponAnimationMath.Compose(
				document.Calibration.PhysicalTransform,
				document.Calibration.FramingTransform );
			ApplyTestPose();
		}

		if ( _armsRenderer.IsValid() )
		{
			_armsRenderer!.BoneMergeTarget = null;
			_armsRenderer.WorldTransform = Transform.Zero;
		}

		DrawMeasurement();
		DrawAnchors();
		if ( document.Workspace.ShowSkeleton )
			DrawRendererSkeleton( _sourceRenderer, WeaponAnimatorTheme.Amber );

		if ( CalibrationSelection.TryGetAnchor( document.Workspace.SelectedControl, out var anchorKind )
			&& document.Calibration.GetAnchor( anchorKind ) is { } anchor )
			DrawSelectedAnchorControl( anchor );
		else if ( !string.IsNullOrWhiteSpace( document.Workspace.SelectedBone ) )
			DrawSelectedSourceBoneControl();
		else
			DrawWholeRigControl();
	}

	private void DrawAnimation()
	{
		if ( !_hostRenderer.IsValid() || _hostSkeleton is null )
			return;

		var document = _controller.Document;
		var clip = document.GetSelectedClip();
		var pose = AnimationPoseEvaluator.Evaluate(
			document,
			_hostSkeleton,
			clip,
			document.Workspace.TimelineTime,
			includeWorkingPose: true );

		_hostRenderer!.ClearPhysicsBones();
		foreach ( var bone in _hostRenderer.Model.Bones.AllBones )
		{
			if ( pose.Model.TryGetValue( bone.Name, out var modelTransform ) )
				_hostRenderer.SetBoneTransform( bone, modelTransform );
		}
		SuppressHostRendering();
		ApplyWeaponPoseToSourceRenderer( pose );
		ApplyArmPoseToRenderer( pose );

		document.Binding.PrimaryHand.Reachable = pose.PrimaryReachable;
		document.Binding.SupportHand.Reachable = pose.SupportReachable;
		DrawGripTethers( pose );
		if ( document.Workspace.ShowSkeleton )
			DrawHostSkeleton( pose, 1.0f, useRenderedArms: true );
		if ( document.Workspace.ShowOnionSkins && clip is not null )
			DrawOnionSkins( clip );
		DrawAnimationControl();
	}

	private void ApplyWeaponPoseToSourceRenderer( EvaluatedPose pose )
	{
		if ( !_sourceRenderer.IsValid() || _hostSkeleton is null )
			return;

		var document = _controller.Document;
		var sourceRoot = document.Rig.FindBone( document.Rig.SourceSkeletonRootId );
		var rootTransform = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );
		if ( sourceRoot is not null
			&& pose.Model.TryGetValue( "weapon_root", out var desiredRootWorld ) )
		{
			rootTransform = WeaponPoseProjection.SolveRendererTransform(
				sourceRoot.BindModelTransform,
				desiredRootWorld );
		}

		_sourceRenderer!.BoneMergeTarget = null;
		_sourceRenderer.WorldTransform = rootTransform;
		_sourceRenderer.ClearPhysicsBones();
		foreach ( var definition in document.Rig.RetainedBones() )
		{
			if ( definition.Id.Equals(
				document.Rig.SourceSkeletonRootId,
				StringComparison.OrdinalIgnoreCase ) )
				continue;

			var sourceBone = _sourceRenderer.Model.Bones.GetBone( definition.Name );
			if ( sourceBone is not null
				&& WeaponPoseProjection.TryGetSourceWorldOverride(
					document,
					pose,
					definition,
					out var transform )
				&& _hostSkeleton.ByName.TryGetValue( definition.Name, out var hostBone )
				&& pose.Local.TryGetValue( definition.Name, out var currentLocal )
				&& !WeaponPoseProjection.TransformNear(
					currentLocal,
					_hostSkeleton.GetBindLocal( hostBone ) ) )
			{
				// Native bind transforms remain untouched; only authored deltas use overrides.
				_sourceRenderer.SetBoneTransform(
					sourceBone,
					_sourceRenderer.WorldTransform.ToLocal( transform ) );
			}
		}
		LogSourcePoseDiagnostics( pose );
	}

	private void ApplyArmPoseToRenderer( EvaluatedPose pose )
	{
		if ( !_armsRenderer.IsValid() )
			return;

		_armsRenderer!.BoneMergeTarget = null;
		_armsRenderer.WorldTransform = Transform.Zero;
		_armsRenderer.ClearPhysicsBones();
		foreach ( var bone in _armsRenderer.Model.Bones.AllBones )
		{
			if ( pose.Model.TryGetValue( bone.Name, out var modelTransform ) )
				_armsRenderer.SetBoneTransform( bone, modelTransform );
		}

		LogArmPoseDiagnostics( pose );
	}

	private void LogArmPoseDiagnostics( EvaluatedPose pose )
	{
		if ( _armPoseDiagnosticsLogged || !_armsRenderer.IsValid() )
			return;
		if ( ++_armPoseDiagnosticFrames < 3 )
			return;
		_armPoseDiagnosticsLogged = true;

		var compared = 0;
		var mismatches = 0;
		foreach ( var bone in _armsRenderer!.Model.Bones.AllBones )
		{
			if ( !pose.Model.TryGetValue( bone.Name, out var expected )
				|| !_armsRenderer.TryGetBoneTransform( bone, out var actual ) )
				continue;

			compared++;
			if ( WeaponPoseProjection.TransformNear( expected, actual, 0.001f ) )
				continue;
			mismatches++;
			if ( mismatches <= 4 )
			{
				Log.Warning(
					$"[Weapon Animator] arm pose mismatch '{bone.Name}': "
					+ $"expected={expected}, actual={actual}." );
			}
		}

		Log.Info(
			$"[Weapon Animator] arm pose bridge checked {compared} bones; "
			+ $"{mismatches} renderer override mismatches." );
	}

	private void LogSourcePoseDiagnostics( EvaluatedPose pose )
	{
		if ( _sourcePoseDiagnosticsLogged || !_sourceRenderer.IsValid() )
			return;
		if ( ++_sourcePoseDiagnosticFrames < 3 )
			return;
		_sourcePoseDiagnosticsLogged = true;

		var compared = 0;
		var mismatches = 0;
		foreach ( var definition in _controller.Document.Rig.RetainedBones() )
		{
			var sourceBone = _sourceRenderer!.Model.Bones.GetBone( definition.Name );
			if ( sourceBone is null
				|| !WeaponPoseProjection.TryGetSourceWorldOverride(
					_controller.Document,
					pose,
					definition,
					out var expected )
				|| !_sourceRenderer.TryGetBoneTransform( sourceBone, out var actual ) )
				continue;

			compared++;
			var positionDelta = expected.Position.Distance( actual.Position );
			var rotationDelta = MathF.Max(
				(expected.Rotation.Forward - actual.Rotation.Forward).Length,
				(expected.Rotation.Up - actual.Rotation.Up).Length );
			var scaleDelta = (expected.Scale - actual.Scale).Length;
			if ( positionDelta <= 0.001f
				&& rotationDelta <= 0.001f
				&& scaleDelta <= 0.001f )
				continue;

			mismatches++;
			Log.Warning(
				$"[Weapon Animator] source pose mismatch '{definition.Name}': "
				+ $"position={positionDelta:0.######}, "
				+ $"rotation={rotationDelta:0.######}, "
				+ $"scale={scaleDelta:0.######}; "
				+ $"expected={expected}, actual={actual}." );
		}

		Log.Info(
			$"[Weapon Animator] source pose bridge checked {compared} retained bones; "
			+ $"{mismatches} renderer override mismatches." );
		if ( _hostSkeleton is not null
			&& _hostSkeleton.ByName.TryGetValue( "root", out var hostRoot )
			&& _hostSkeleton.ByName.TryGetValue( "weapon_root", out var weaponRoot )
			&& pose.Model.TryGetValue( "weapon_root", out var rootWorld )
			&& pose.Local.TryGetValue( "weapon_root", out var rootLocal ) )
		{
			Log.Info(
				$"[Weapon Animator] root bridge: hostRootBind={hostRoot.BindModelTransform}, "
				+ $"weaponRootBindModel={weaponRoot.BindModelTransform}, "
				+ $"weaponRootBindLocal={_hostSkeleton.GetBindLocal( weaponRoot )}, "
				+ $"poseRootWorld={rootWorld}, poseRootLocal={rootLocal}, "
				+ $"sourceRenderer={_sourceRenderer!.WorldTransform}." );
		}
	}

	private bool RepairLegacyIdleIfNeeded()
	{
		if ( _legacyIdleRepairVersionChecked == LegacyIdleRepairVersion
			|| _controller.Document.ActiveStage != WeaponAnimatorStage.Animate )
			return false;

		_legacyIdleRepairVersionChecked = LegacyIdleRepairVersion;
		var repaired = false;
		_controller.Mutate(
			"Repair generated Idle bind pose",
			document =>
			{
				var repairedLegacy = WeaponAnimationMigration.RepairLegacyIdleBindPose(
					document,
					_hostSkeleton );
				var repairedSelectionWrites = _hostSkeleton is not null
					&& IdleBindPoseService.RepairUnintendedSelectionWrites(
						document,
						_hostSkeleton );
				repaired = repairedLegacy || repairedSelectionWrites;
			} );
		if ( !repaired )
			return false;

		LegacyIdleRepaired?.Invoke();
		StatusChanged?.Invoke(
			"Restored the generated Idle clip to the current calibrated bind pose. "
			+ "A versioned backup will be created on save." );
		return true;
	}

	private void SuppressHostRendering()
	{
		if ( !_hostRenderer.IsValid() )
			return;

		// The host owns bones only. Its carrier mesh must never enter the authoring viewport.
		_hostRenderer!.Tint = Color.Transparent;
		_hostRenderer.SceneObject.RenderingEnabled = false;
	}

	private void OnSelectionChanged()
	{
		Update();
		if ( _controller.Document.ActiveStage != WeaponAnimatorStage.Animate )
			return;

		var selected = _controller.Document.Workspace.SelectedBone;
		if ( !selected.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase )
			|| selected.Equals( _lastDiagnosticSelection, StringComparison.OrdinalIgnoreCase ) )
			return;

		_lastDiagnosticSelection = selected;
		var clip = _controller.Document.GetSelectedClip();
		var rootTrack = clip?.Tracks.FirstOrDefault( x =>
			x.Target.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase ) );
		Log.Info(
			$"[Weapon Animator] Preview diagnostic: selected=weapon_root, "
			+ $"sourceModel={_loadedSource}, hostModel={_loadedHost}, "
			+ $"sourceScale={_sourceRenderer?.WorldTransform.Scale}, "
			+ $"hostRendering={_hostRenderer?.SceneObject.RenderingEnabled}, "
			+ $"rootKeys={rootTrack?.Keys.Count ?? 0}, "
			+ $"workingOverride={_controller.Document.Workspace.GetWorkingPose(
				clip?.Id ?? Guid.Empty,
				"weapon_root" ) is not null}." );
	}

	private void ApplyTestPose()
	{
		if ( !_sourceRenderer.IsValid() )
			return;

		_sourceRenderer!.ClearPhysicsBones();
		foreach ( var item in _testPose )
		{
			var bone = _sourceRenderer.Model.Bones.GetBone( item.Key );
			if ( bone is not null )
				_sourceRenderer.SetBoneTransform( bone, item.Value );
		}
	}

	private void DrawRendererSkeleton( SkinnedModelRenderer? renderer, Color color )
	{
		if ( !renderer.IsValid() || renderer!.Model is null )
			return;

		foreach ( var bone in renderer.Model.Bones.AllBones )
		{
			if ( !renderer.TryGetBoneTransform( bone, out var transform ) )
				continue;

			if ( bone.Parent is not null
				&& renderer.TryGetBoneTransform( bone.Parent, out var parent ) )
			{
				Gizmo.Draw.Color = color.WithAlpha( 0.55f );
				Gizmo.Draw.Line( parent.Position, transform.Position );
			}

			using var scope = Gizmo.Scope( $"source_bone:{bone.Name}", transform );
			var selected = bone.Name == _controller.Document.Workspace.SelectedBone;
			var radius = Math.Clamp( transform.Position.Distance( _camera.WorldPosition ) / 150.0f, 0.1f, 0.7f );
			Gizmo.Draw.Color = selected ? Color.White : color;
			Gizmo.Draw.SolidSphere( Vector3.Zero, selected ? radius * 0.7f : radius * 0.35f, 6, 4 );
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.Sphere( new Sphere( Vector3.Zero, radius ) );
			if ( Gizmo.IsHovered )
			{
				Gizmo.Draw.ScreenText( bone.Name, transform.Position, new Vector2( 10, -10 ) );
				if ( Gizmo.WasLeftMousePressed )
					_controller.SelectBone( bone.Name );
			}
		}
	}

	private void DrawHostSkeleton(
		EvaluatedPose pose,
		float alpha,
		bool useRenderedArms = false )
	{
		if ( _hostSkeleton is null )
			return;

		foreach ( var bone in _hostSkeleton.Bones )
		{
			if ( !TryGetDisplayedBoneTransform(
				pose,
				bone,
				useRenderedArms,
				out var transform ) )
				continue;
			var color = bone.IsWeaponBone ? WeaponAnimatorTheme.Amber : WeaponAnimatorTheme.Cyan;
			if ( !string.IsNullOrWhiteSpace( bone.ParentName )
				&& _hostSkeleton.ByName.TryGetValue( bone.ParentName, out var parentBone )
				&& TryGetDisplayedBoneTransform(
					pose,
					parentBone,
					useRenderedArms,
					out var parent ) )
			{
				Gizmo.Draw.Color = color.WithAlpha( 0.45f * alpha );
				Gizmo.Draw.Line( parent.Position, transform.Position );
			}

			using var scope = Gizmo.Scope( $"host_bone:{bone.Name}", transform );
			var selected = bone.Name == _controller.Document.Workspace.SelectedBone;
			var radius = Math.Clamp( transform.Position.Distance( _camera.WorldPosition ) / 180.0f, 0.08f, 0.45f );
			Gizmo.Draw.Color = (selected ? Color.White : color).WithAlpha( alpha );
			Gizmo.Draw.SolidSphere( 0, selected ? radius * 0.7f : radius * 0.3f, 5, 4 );
			Gizmo.Hitbox.Sphere( new Sphere( 0, radius ) );
			if ( Gizmo.IsHovered && Gizmo.WasLeftMousePressed )
				_controller.SelectBone( bone.Name );
		}
	}

	private bool TryGetDisplayedBoneTransform(
		EvaluatedPose pose,
		HostBone bone,
		bool useRenderedArms,
		out Transform transform )
	{
		if ( useRenderedArms && !bone.IsWeaponBone && _armsRenderer.IsValid() )
		{
			var rendererBone = _armsRenderer!.Model.Bones.GetBone( bone.Name );
			if ( rendererBone is not null
				&& _armsRenderer.TryGetBoneTransform( rendererBone, out transform ) )
				return true;
		}

		return pose.Model.TryGetValue( bone.Name, out transform );
	}

	private void DrawOnionSkins( WeaponAnimationClip clip )
	{
		if ( _hostSkeleton is null )
			return;
		var step = 1.0f / MathF.Max( clip.SampleRate, 1 );
		foreach ( var offset in new[] { -step, step } )
		{
			var time = Math.Clamp(
				_controller.Document.Workspace.TimelineTime + offset,
				0,
				clip.Duration );
			var onion = AnimationPoseEvaluator.Evaluate(
				_controller.Document,
				_hostSkeleton,
				clip,
				time );
			DrawHostSkeleton( onion, 0.18f );
		}
	}

	private void DrawGripTethers( EvaluatedPose pose )
	{
		if ( _controller.Document.Binding.PrimaryHand.IsBound )
		{
			DrawTether(
				_controller.Document.Binding.PrimaryHand,
				pose,
				_controller.Document.Binding.PrimaryHand.Reachable,
				"hand_R" );
		}
		if ( _controller.Document.Binding.Configuration == GripConfiguration.TwoHanded )
		{
			if ( _controller.Document.Binding.SupportHand.IsBound )
			{
				DrawTether(
					_controller.Document.Binding.SupportHand,
					pose,
					_controller.Document.Binding.SupportHand.Reachable,
					"hand_L" );
			}
		}
	}

	private static void DrawTether(
		RigTarget target,
		EvaluatedPose pose,
		bool reachable,
		string handBone )
	{
		if ( !pose.Model.TryGetValue( handBone, out var hand ) )
			return;
		var targetTransform = target.Transform;
		if ( !string.IsNullOrWhiteSpace( target.AttachedBone )
			&& pose.Model.TryGetValue( target.AttachedBone, out var attached ) )
		{
			targetTransform = new Transform(
				attached.PointToWorld( target.Transform.Position ),
				attached.Rotation * target.Transform.Rotation );
		}

		Gizmo.Draw.Color = reachable ? WeaponAnimatorTheme.Green : WeaponAnimatorTheme.Coral;
		Gizmo.Draw.LineThickness = 2.5f;
		Gizmo.Draw.Line( hand.Position, targetTransform.Position );
		Gizmo.Draw.SolidSphere( targetTransform.Position, 0.18f, 8, 6 );
	}

	private void DrawSelectedSourceBoneControl()
	{
		if ( !_sourceRenderer.IsValid() )
			return;
		var selected = _controller.Document.Workspace.SelectedBone;
		var bone = _sourceRenderer!.Model.Bones.GetBone( selected );
		if ( bone is null || !_sourceRenderer.TryGetBoneTransform( bone, out var world ) )
			return;

		using var scope = Gizmo.Scope( $"test:{selected}", world );
		if ( TransformMode == WeaponAnimatorTransformMode.Rotate )
		{
			if ( Gizmo.Control.Rotate( "rotate", Rotation.Identity, out var delta ) )
			{
				var changed = world;
				changed.Rotation *= SnapRotation( delta );
				_testPose[selected] = changed;
			}
		}
		else if ( TransformMode == WeaponAnimatorTransformMode.Move
			&& Gizmo.Control.Position( "move", world.Position, out var position ) )
		{
			position = SnapAbsolutePosition( world.Position, position, world.Rotation );
			var changed = world.WithPosition( position );
			_testPose[selected] = changed;
		}
		else if ( TransformMode == WeaponAnimatorTransformMode.Scale )
		{
			var uniformScale = MathF.Max( world.Scale.x, 0.0001f );
			if ( Gizmo.Control.Scale( "scale", uniformScale, out var scale ) )
			{
				var ratio = MathF.Max( scale, 0.0001f ) / uniformScale;
				_testPose[selected] = world.WithScale(
					ClampScale( world.Scale * ratio ) );
			}
		}
	}

	private void DrawSelectedAnchorControl( WeaponAnchor anchor )
	{
		if ( !_sourceRenderer.IsValid() )
			return;

		var sourceTransform = _sourceRenderer!.WorldTransform;
		var world = new Transform(
			sourceTransform.PointToWorld( anchor.LocalPosition ),
			sourceTransform.Rotation * anchor.LocalRotation );
		using var scope = Gizmo.Scope( $"anchor_control:{anchor.Kind}", world );
		Gizmo.Draw.Color = AnchorColor( anchor.Kind );
		Gizmo.Draw.LineSphere( new Sphere( Vector3.Zero, 0.24f ) );

		if ( TransformMode == WeaponAnimatorTransformMode.Rotate )
		{
			if ( Gizmo.Control.Rotate( "anchor_rotate", Rotation.Identity, out var delta ) )
			{
				_controller.Mutate( $"Rotate {anchor.Name} anchor", document =>
				{
					var selected = document.Calibration.GetAnchor( anchor.Kind );
					if ( selected is null )
						return;
					selected.LocalRotation *= SnapRotation( delta );
					document.Calibration.Confirmed = false;
				} );
			}
		}
		else if ( TransformMode == WeaponAnimatorTransformMode.Move
			&& Gizmo.Control.Position( "anchor_move", world.Position, out var position ) )
		{
			position = SnapAbsolutePosition( world.Position, position, world.Rotation );
			_controller.Mutate( $"Move {anchor.Name} anchor", document =>
			{
				var selected = document.Calibration.GetAnchor( anchor.Kind );
				if ( selected is null )
					return;
				selected.LocalPosition = sourceTransform.PointToLocal( position );
				document.Calibration.Confirmed = false;
			} );
		}
	}

	private void DrawWholeRigControl()
	{
		var document = _controller.Document;
		var transform = document.Workspace.FirstPersonPreview
			? document.Calibration.FramingTransform
			: document.Calibration.PhysicalTransform;
		using var scope = Gizmo.Scope( "whole_rig", transform );

		if ( TransformMode == WeaponAnimatorTransformMode.Rotate )
		{
			if ( Gizmo.Control.Rotate( "rig_rotate", Rotation.Identity, out var delta ) )
			{
				_controller.Mutate( "Refine whole-rig rotation", d =>
				{
					var target = d.Workspace.FirstPersonPreview
						? d.Calibration.FramingTransform
						: d.Calibration.PhysicalTransform;
					target.Rotation *= SnapRotation( delta );
					if ( d.Workspace.FirstPersonPreview )
						d.Calibration.FramingTransform = target;
					else
						d.Calibration.PhysicalTransform = target;
				} );
			}
		}
		else if ( TransformMode == WeaponAnimatorTransformMode.Move
			&& Gizmo.Control.Position( "rig_move", transform.Position, out var position ) )
		{
			position = SnapAbsolutePosition( transform.Position, position, transform.Rotation );
			_controller.Mutate( "Refine whole-rig position", d =>
			{
				if ( d.Workspace.FirstPersonPreview )
					d.Calibration.FramingTransform = d.Calibration.FramingTransform.WithPosition( position );
				else
					d.Calibration.PhysicalTransform = d.Calibration.PhysicalTransform.WithPosition( position );
			} );
		}
		else if ( TransformMode == WeaponAnimatorTransformMode.Scale )
		{
			var uniformScale = MathF.Max( transform.Scale.x, 0.0001f );
			if ( Gizmo.Control.Scale( "rig_scale", uniformScale, out var scale ) )
			{
				scale = MathF.Max( scale, 0.0001f );
				_controller.Mutate( "Refine whole-rig scale", d =>
				{
					var target = d.Workspace.FirstPersonPreview
						? d.Calibration.FramingTransform
						: d.Calibration.PhysicalTransform;
					target.Scale = new Vector3( scale );
					if ( d.Workspace.FirstPersonPreview )
						d.Calibration.FramingTransform = target;
					else
						d.Calibration.PhysicalTransform = target;
				} );
			}
		}
	}

	private void DrawAnimationControl()
	{
		var context = SelectionTransformContext.Resolve( _controller );
		if ( context is null )
			return;

		var dragging = _animationGizmoTarget.Equals(
			context.Target,
			StringComparison.OrdinalIgnoreCase );
		var startWorld = dragging ? _animationGizmoStartWorld : context.WorldTransform;
		var basis = context.LocalSpace
			? startWorld.Rotation
			: Rotation.Identity;
		var gizmoTransform = new Transform( startWorld.Position, basis );
		using var scope = Gizmo.Scope( $"animate:{context.Target}", gizmoTransform );
		Gizmo.Draw.Color = context.Kind == RigControlKind.Weapon
			? WeaponAnimatorTheme.Amber
			: WeaponAnimatorTheme.Cyan;
		Gizmo.Draw.LineSphere( new Sphere( 0, 0.25f ) );

		if ( TransformMode == WeaponAnimatorTransformMode.Rotate )
		{
			if ( Gizmo.Control.Rotate( "rotate", Rotation.Identity, out var delta ) )
			{
				BeginAnimationGizmoDrag( context );
				var snapped = SnapRotation( delta );
				var local = _animationGizmoStartLocal;
				if ( context.LocalSpace )
				{
					local.Rotation = (_animationGizmoStartLocal.Rotation * snapped).Normal;
				}
				else
				{
					var editedWorld = _animationGizmoStartWorld.WithRotation(
						(snapped * _animationGizmoStartWorld.Rotation).Normal );
					local = WorldToLocal( editedWorld, _animationGizmoStartParent );
				}
				_controller.UpdateTransformEditContinuous(
					context.Target,
					context.Kind,
					local );
			}
		}
		else if ( TransformMode == WeaponAnimatorTransformMode.Move
			&& Gizmo.Control.Position( "move", Vector3.Zero, out var delta, basis ) )
		{
			BeginAnimationGizmoDrag( context );
			_animationGizmoMoveDelta += delta;
			var position = SnapPositionDelta(
				_animationGizmoStartWorld.Position,
				_animationGizmoMoveDelta,
				basis );
			var editedWorld = _animationGizmoStartWorld.WithPosition( position );
			var local = WorldToLocal( editedWorld, _animationGizmoStartParent );
			_controller.UpdateTransformEditContinuous(
				context.Target,
				context.Kind,
				local );
		}
		else if ( TransformMode == WeaponAnimatorTransformMode.Scale
			&& Gizmo.Control.Scale( "scale", Vector3.Zero, out var scaleDelta, basis ) )
		{
			BeginAnimationGizmoDrag( context );
			_animationGizmoScaleDelta += scaleDelta / 0.01f;
			var local = ScaleFromStart(
				_animationGizmoStartLocal,
				_animationGizmoStartWorld,
				_animationGizmoStartParent,
				context.LocalSpace,
				_animationGizmoScaleDelta );
			_controller.UpdateTransformEditContinuous(
				context.Target,
				context.Kind,
				local );
		}
	}

	private void BeginAnimationGizmoDrag( SelectionTransformContext context )
	{
		if ( _animationGizmoTarget.Equals(
			context.Target,
			StringComparison.OrdinalIgnoreCase )
			&& _animationGizmoKind == context.Kind )
			return;

		EndAnimationGizmoDrag();
		_animationGizmoTarget = context.Target;
		_animationGizmoKind = context.Kind;
		_animationGizmoStartLocal = context.LocalTransform;
		_animationGizmoStartWorld = context.WorldTransform;
		_animationGizmoStartParent = context.ParentTransform;
		_animationGizmoMoveDelta = Vector3.Zero;
		_animationGizmoScaleDelta = Vector3.Zero;
		_controller.BeginContinuousEdit(
			$"{TransformModeName( TransformMode )} {context.Target}" );
	}

	private void FinishAnimationGizmoDragIfReleased()
	{
		if ( string.IsNullOrWhiteSpace( _animationGizmoTarget )
			|| Gizmo.Pressed.Any )
			return;

		EndAnimationGizmoDrag();
	}

	private void EndAnimationGizmoDrag()
	{
		if ( string.IsNullOrWhiteSpace( _animationGizmoTarget ) )
			return;

		_animationGizmoTarget = "";
		_animationGizmoMoveDelta = Vector3.Zero;
		_animationGizmoScaleDelta = Vector3.Zero;
		_animationGizmoStartParent = null;
		_controller.EndContinuousEdit();
	}

	private Rotation SnapRotation( Rotation delta ) =>
		_controller.Document.Workspace.SnapRotation ? Gizmo.Snap( delta ) : delta;

	private Vector3 SnapPositionDelta( Vector3 start, Vector3 movement, Rotation localSpace )
	{
		if ( !_controller.Document.Workspace.SnapPosition )
			return start + movement;

		return Gizmo.Snap( start, movement, localSpace );
	}

	private Vector3 SnapAbsolutePosition(
		Vector3 start,
		Vector3 position,
		Rotation localSpace )
	{
		if ( !_controller.Document.Workspace.SnapPosition )
			return position;

		return Gizmo.Snap( start, position - start, localSpace );
	}

	internal static Transform WorldToLocal( Transform world, Transform? parent ) =>
		parent is null ? world : parent.Value.ToLocal( world );

	internal static Transform ScaleFromStart(
		Transform startLocal,
		Transform startWorld,
		Transform? parent,
		bool localSpace,
		Vector3 accumulatedDelta )
	{
		var factor = ClampScale(
			Vector3.One + accumulatedDelta * ScaleGizmoSensitivity );
		if ( localSpace )
			return startLocal.WithScale(
				ClampScale( startLocal.Scale * factor ) );

		var editedWorld = startWorld.WithScale(
			ClampScale( startWorld.Scale * factor ) );
		return WorldToLocal( editedWorld, parent );
	}

	private static Vector3 ClampScale( Vector3 scale ) =>
		new(
			MathF.Max( scale.x, 0.0001f ),
			MathF.Max( scale.y, 0.0001f ),
			MathF.Max( scale.z, 0.0001f ) );

	private void DrawMeasurement()
	{
		var measurement = _controller.Document.Calibration.Measurement;
		if ( !measurement.HasFirstPoint )
			return;

		var transform = _sourceRenderer?.WorldTransform ?? Transform.Zero;
		var a = transform.PointToWorld( measurement.FirstPoint );
		Gizmo.Draw.Color = WeaponAnimatorTheme.Cyan;
		Gizmo.Draw.SolidSphere( a, 0.15f, 8, 6 );
		if ( !measurement.HasSecondPoint )
			return;

		var b = transform.PointToWorld( measurement.SecondPoint );
		Gizmo.Draw.SolidSphere( b, 0.15f, 8, 6 );
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.Line( a, b );
		Gizmo.Draw.ScreenText(
			$"{measurement.FirstPoint.Distance( measurement.SecondPoint ):0.###} source units",
			(a + b) * 0.5f,
			new Vector2( 8, -8 ) );
	}

	private void DrawAnchors()
	{
		var transform = _sourceRenderer?.WorldTransform ?? Transform.Zero;
		foreach ( var anchor in _controller.Document.Calibration.Anchors )
		{
			var world = transform.PointToWorld( anchor.LocalPosition );
			var color = AnchorColor( anchor.Kind );
			var markerScale = Math.Clamp( world.Distance( _camera.WorldPosition ) / 75.0f, 0.45f, 1.4f );
			var labelOffset = AnchorLabelOffset( anchor.Kind );
			var leaderEnd = world
				+ _camera.WorldRotation.Right * labelOffset.x * markerScale
				+ _camera.WorldRotation.Up * labelOffset.y * markerScale;
			Gizmo.Draw.Color = color.WithAlpha( 0.75f );
			Gizmo.Draw.LineThickness = 1.5f;
			Gizmo.Draw.Line( world, leaderEnd );
			Gizmo.Draw.ScreenText(
				$"[{AnchorCode( anchor.Kind )}] {CalibrationSelection.DisplayName( anchor.Kind ).ToUpperInvariant()}",
				leaderEnd,
				new Vector2( 6, -6 ),
				size: 11 );
			using var scope = Gizmo.Scope(
				$"anchor:{anchor.Kind}",
				new Transform( world, transform.Rotation * anchor.LocalRotation ) );
			var selected = _controller.Document.Workspace.SelectedControl == CalibrationSelection.Anchor( anchor.Kind );
			Gizmo.Draw.Color = selected ? Color.White : color;
			Gizmo.Draw.SolidSphere( Vector3.Zero, selected ? 0.24f : 0.18f, 8, 6 );
			Gizmo.Hitbox.Sphere( new Sphere( Vector3.Zero, 0.32f ) );
			if ( Gizmo.IsHovered && Gizmo.WasLeftMousePressed )
				_controller.SelectControl( CalibrationSelection.Anchor( anchor.Kind ) );
		}

		var rear = _controller.Document.Calibration.GetAnchor( AnchorKind.RearBore );
		var front = _controller.Document.Calibration.GetAnchor( AnchorKind.FrontBore );
		if ( rear is null || front is null )
			return;
		Gizmo.Draw.Color = WeaponAnimatorTheme.Amber;
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.Arrow(
			transform.PointToWorld( rear.LocalPosition ),
			transform.PointToWorld( front.LocalPosition ),
			0.6f,
			0.25f );
	}

	private void DrawScreenGuides()
	{
		var document = _controller.Document;
		if ( !document.Workspace.ShowGuides )
			return;

		var viewport = new Rect( 0, 0, Size.x, Size.y );
		var guideAspect = GuideAspect( document.Calibration.AspectGuide );
		var viewportAspect = Size.x / MathF.Max( Size.y, 1 );
		Rect guide;
		if ( viewportAspect > guideAspect )
		{
			var width = Size.y * guideAspect;
			guide = new Rect( (Size.x - width) * 0.5f, 0, width, Size.y );
		}
		else
		{
			var height = Size.x / guideAspect;
			guide = new Rect( 0, (Size.y - height) * 0.5f, Size.x, height );
		}

		Gizmo.Draw.ScreenRect(
			viewport,
			Color.Transparent,
			borderColor: Color.White.WithAlpha( 0.05f ),
			borderSize: new Vector4( 1 ) );
		Gizmo.Draw.ScreenRect(
			guide,
			Color.Transparent,
			borderColor: Color.White.WithAlpha( 0.35f ),
			borderSize: new Vector4( 1 ) );

		if ( document.Calibration.ShowSafeArea )
		{
			Gizmo.Draw.ScreenRect(
				guide.Shrink( guide.Width * 0.05f, guide.Height * 0.05f ),
				Color.Transparent,
				borderColor: WeaponAnimatorTheme.Cyan.WithAlpha( 0.24f ),
				borderSize: new Vector4( 1 ) );
		}

		if ( document.Calibration.ShowCrosshair )
		{
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.65f );
			Gizmo.Draw.ScreenText( "+", guide.Center, size: 19, flags: TextFlag.Center );
		}

		Gizmo.Draw.Color = WeaponAnimatorTheme.Muted;
		var mode = document.Workspace.FirstPersonPreview
			? "VIEWMODEL CAMERA"
			: document.Workspace.FreeLookCamera
				? "FREE LOOK"
				: "ORBIT";
		Gizmo.Draw.ScreenText(
			$"{mode} · {document.Calibration.AspectGuide} · {document.Calibration.HorizontalFov:0}° HFOV",
			new Vector2( 12, 52 ),
			size: 10 );
	}

	private void DrawViewportToolReadout()
	{
		Gizmo.Draw.ScreenRect(
			new Rect( 109, 15, 1, 18 ),
			Color.White.WithAlpha( 0.14f ) );
		Gizmo.Draw.ScreenRect(
			new Rect( MathF.Max( Width - 47, 66 ), 15, 1, 18 ),
			Color.White.WithAlpha( 0.14f ) );
		Gizmo.Draw.ScreenRect(
			TransformReadoutRect,
			WeaponAnimatorTheme.Background.WithAlpha( 0.25f ) );
		var text = new TextRendering.Scope
		{
			Text = _transformModeText,
			TextColor = WeaponAnimatorTheme.Text.WithAlpha( 0.78f ),
			FontSize = 10 * global::Editor.Application.DpiScale,
			FontName = "Inter",
			FontWeight = 500,
			LineHeight = 1
		};
		Gizmo.Draw.ScreenText(
			text,
			new Vector2(
				TransformReadoutRect.Left + 6,
				TransformReadoutRect.Center.y ),
			TextFlag.LeftCenter );
	}

	private void DrawCameraSpeedOverlay()
	{
		if ( _sinceCameraSpeedChanged >= 1.8f )
			return;

		var elapsed = (float)_sinceCameraSpeedChanged;
		var alpha = elapsed <= 0.9f
			? 1.0f
			: 1.0f - Math.Clamp( (elapsed - 0.9f) / 0.9f, 0, 1 );
		var rect = new Rect(
			MathF.Max( (Width - 150) * 0.5f, 0 ),
			Width >= 720 ? 10 : 46,
			150,
			28 );
		Gizmo.Draw.ScreenRect(
			rect,
			WeaponAnimatorTheme.Background.WithAlpha( 0.55f * alpha ) );
		var text = new TextRendering.Scope
		{
			Text = $"CAMERA SPEED  {_controller.Document.Workspace.CameraMoveSpeed:0.##}×",
			TextColor = WeaponAnimatorTheme.Text.WithAlpha( 0.9f * alpha ),
			FontSize = 10 * global::Editor.Application.DpiScale,
			FontName = "Inter",
			FontWeight = 500,
			LineHeight = 1
		};
		Gizmo.Draw.ScreenText( text, rect.Center, TextFlag.Center );
	}

	private bool TryPickSourceSurface( Vector2 localPosition, out Vector3 modelPosition )
	{
		modelPosition = default;
		if ( !_sourceRenderer.IsValid() || _sourceRenderer!.Model is null )
			return false;

		var ray = GetRay( localPosition );
		var localRay = ray.ToLocal( _sourceRenderer.WorldTransform );
		var trace = _sourceRenderer.Model.Trace.Ray( localRay, 8192 ).Run();
		if ( !trace.Hit )
			return false;

		modelPosition = trace.HitPosition;
		return true;
	}

	private void ApplyPickedPoint( Vector3 localPosition )
	{
		var mode = PickMode;
		PickMode = ViewportPickMode.None;
		_controller.Mutate( $"Set {PickLabel( mode )}", document =>
		{
			var measurement = document.Calibration.Measurement;
			switch ( mode )
			{
				case ViewportPickMode.MeasurementFirst:
					measurement.FirstPoint = localPosition;
					measurement.HasFirstPoint = true;
					break;
				case ViewportPickMode.MeasurementSecond:
					measurement.SecondPoint = localPosition;
					measurement.HasSecondPoint = true;
					break;
				default:
					var kind = PickAnchorKind( mode );
					document.Calibration.SetAnchor( new WeaponAnchor
					{
						Kind = kind,
						Name = CalibrationSelection.DisplayName( kind ),
						BoneName = document.Workspace.SelectedBone,
						LocalPosition = localPosition
					} );
					document.Calibration.Confirmed = false;
					break;
			}
		} );
		if ( mode is not ViewportPickMode.MeasurementFirst
			and not ViewportPickMode.MeasurementSecond )
		{
			_controller.SelectControl( CalibrationSelection.Anchor( PickAnchorKind( mode ) ) );
		}
		StatusChanged?.Invoke( $"{PickLabel( mode )} set at {localPosition}." );
	}

	private static AnchorKind PickAnchorKind( ViewportPickMode mode ) => mode switch
	{
		ViewportPickMode.GripAnchor => AnchorKind.Grip,
		ViewportPickMode.RearBoreAnchor => AnchorKind.RearBore,
		ViewportPickMode.FrontBoreAnchor => AnchorKind.FrontBore,
		ViewportPickMode.MuzzleAnchor => AnchorKind.Muzzle,
		ViewportPickMode.EjectAnchor => AnchorKind.Eject,
		_ => AnchorKind.Custom
	};

	private static string PickLabel( ViewportPickMode mode ) => mode switch
	{
		ViewportPickMode.MeasurementFirst => "measurement point A",
		ViewportPickMode.MeasurementSecond => "measurement point B",
		ViewportPickMode.GripAnchor => "primary grip",
		ViewportPickMode.RearBoreAnchor => "alignment marker — rear",
		ViewportPickMode.FrontBoreAnchor => "alignment marker — front",
		ViewportPickMode.MuzzleAnchor => "muzzle",
		ViewportPickMode.EjectAnchor => "eject",
		_ => "point"
	};

	private static Color AnchorColor( AnchorKind kind ) => kind switch
	{
		AnchorKind.Grip => WeaponAnimatorTheme.Cyan,
		AnchorKind.RearBore => new Color( 0.64f, 0.48f, 0.95f ),
		AnchorKind.FrontBore => WeaponAnimatorTheme.Amber,
		AnchorKind.Muzzle => WeaponAnimatorTheme.Coral,
		AnchorKind.Eject => WeaponAnimatorTheme.Green,
		_ => Color.White
	};

	private static Vector2 AnchorLabelOffset( AnchorKind kind ) => kind switch
	{
		AnchorKind.Grip => new Vector2( -1.8f, 1.1f ),
		AnchorKind.RearBore => new Vector2( 1.5f, 1.7f ),
		AnchorKind.FrontBore => new Vector2( 1.7f, 0.8f ),
		AnchorKind.Muzzle => new Vector2( 2.1f, -0.5f ),
		AnchorKind.Eject => new Vector2( -1.7f, 1.8f ),
		_ => new Vector2( 1.5f, 1.0f )
	};

	private static string AnchorCode( AnchorKind kind ) => kind switch
	{
		AnchorKind.Grip => "G",
		AnchorKind.RearBore => "AR",
		AnchorKind.FrontBore => "AF",
		AnchorKind.Muzzle => "M",
		AnchorKind.Eject => "E",
		_ => "A"
	};

	private static float GuideAspect( string guide ) => guide switch
	{
		"4:3" => 4.0f / 3.0f,
		"21:9" => 21.0f / 9.0f,
		_ => 16.0f / 9.0f
	};
}
