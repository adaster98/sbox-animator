#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public enum ViewportPickMode
{
	None,
	MeasurementFirst,
	MeasurementSecond,
	GripAnchor,
	RearBoreAnchor,
	FrontBoreAnchor,
	MuzzleAnchor,
	EjectAnchor
}

public static class CalibrationSelection
{
	private const string AnchorPrefix = "@anchor:";

	public static string Anchor( AnchorKind kind ) => $"{AnchorPrefix}{kind}";

	public static string DisplayName( AnchorKind kind ) => kind switch
	{
		AnchorKind.Grip => "Primary grip",
		AnchorKind.RearBore => "Alignment marker — rear",
		AnchorKind.FrontBore => "Alignment marker — front",
		AnchorKind.Muzzle => "Muzzle",
		AnchorKind.Eject => "Eject",
		_ => "Custom anchor"
	};

	public static bool TryGetAnchor( string control, out AnchorKind kind )
	{
		kind = default;
		return control.StartsWith( AnchorPrefix, StringComparison.Ordinal )
			&& Enum.TryParse( control[AnchorPrefix.Length..], out kind );
	}
}

internal sealed class ScrubHandle : Widget
{
	private readonly string _text;
	private readonly Color _accent;
	private readonly float _sensitivity;
	private readonly Func<float> _getValue;
	private readonly Action _begin;
	private readonly Action<float> _preview;
	private readonly Action _end;
	private bool _dragging;
	private float _startX;
	private float _startValue;

	public ScrubHandle(
		string text,
		Color accent,
		float sensitivity,
		Func<float> getValue,
		Action begin,
		Action<float> preview,
		Action end,
		Widget parent ) : base( parent )
	{
		_text = text;
		_accent = accent;
		_sensitivity = sensitivity;
		_getValue = getValue;
		_begin = begin;
		_preview = preview;
		_end = end;
		FixedWidth = 20;
		FixedHeight = 26;
		MouseTracking = true;
		Cursor = CursorShape.SizeH;
		ToolTip = $"Drag {_text} horizontally to adjust";
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
		{
			base.OnMousePress( e );
			return;
		}

		_dragging = true;
		_startX = e.ScreenPosition.x;
		_startValue = _getValue();
		_begin();
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !_dragging )
		{
			base.OnMouseMove( e );
			return;
		}

		_preview( _startValue + (e.ScreenPosition.x - _startX) * _sensitivity );
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( !_dragging || e.Button != MouseButtons.Left )
		{
			base.OnMouseReleased( e );
			return;
		}

		_dragging = false;
		_end();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( _accent.WithAlpha( Paint.HasPressed ? 0.42f : Paint.HasMouseOver ? 0.32f : 0.22f ) );
		Paint.DrawRect( LocalRect, 3 );
		Paint.SetPen( _accent.Lighten( 0.25f ) );
		Paint.SetDefaultFont( 10, 650 );
		Paint.DrawText( LocalRect, _text, TextFlag.Center );
	}
}

public sealed class RigAuditPanel : Widget
{
	private readonly WeaponAnimatorController _controller;
	private readonly Widget _boneCanvas;
	private readonly LineEdit _search;
	private readonly Label _sourceStatus;
	private readonly Label _retainedStatus;
	private string _filter = "";
	private bool _showMovable = true;
	private bool _showStructural = true;
	private bool _showIgnored;

	public event Action? ImportRequested;
	public event Action? ResetTestPoseRequested;
	public event Action? RigReviewConfirmed;

	public RigAuditPanel( WeaponAnimatorController controller, Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 8 );
		Layout.Spacing = 6;

		var import = WeaponAnimatorTheme.Button(
			"Import rigged model",
			"file_upload",
			() => ImportRequested?.Invoke(),
			this,
			true );
		Layout.Add( import );

		_sourceStatus = WeaponAnimatorTheme.Label( "No source selected", this, true );
		_sourceStatus.WordWrap = true;
		Layout.Add( _sourceStatus );

		_retainedStatus = WeaponAnimatorTheme.Label( "No weapon subtree selected", this, true );
		_retainedStatus.WordWrap = true;
		Layout.Add( _retainedStatus );

		_search = new LineEdit( this )
		{
			PlaceholderText = "Search bones…",
			FixedHeight = 28
		};
		_search.SetStyles( WeaponAnimatorTheme.InputStyle );
		_search.TextEdited += value =>
		{
			_filter = value?.Trim() ?? "";
			RebuildBones();
		};
		Layout.Add( _search );

		var filters = Row( this );
		filters.Layout.Add( Toggle( filters, "Movable", _showMovable, x => _showMovable = x ) );
		filters.Layout.Add( Toggle( filters, "Structural", _showStructural, x => _showStructural = x ) );
		filters.Layout.Add( Toggle( filters, "Ignored", _showIgnored, x => _showIgnored = x ) );
		Layout.Add( filters );

		var rootActions = Row( this );
		rootActions.Layout.Add( WeaponAnimatorTheme.Button(
			"Set selected as weapon root",
			"account_tree",
			SetSelectedWeaponRoot,
			rootActions ), 1 );
		Layout.Add( rootActions );

		var branchActions = Row( this );
		branchActions.Layout.Add( WeaponAnimatorTheme.Button(
			"Include branch",
			"add",
			IncludeSelectedBranch,
			branchActions ) );
		branchActions.Layout.Add( WeaponAnimatorTheme.Button(
			"Exclude branch",
			"remove",
			ExcludeSelectedBranch,
			branchActions ) );
		Layout.Add( branchActions );

		var scroll = new ScrollArea( this );
		_boneCanvas = new Widget( scroll );
		_boneCanvas.Layout = Layout.Column();
		_boneCanvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin();
		_boneCanvas.Layout.Spacing = 2;
		scroll.Canvas = _boneCanvas;
		Layout.Add( scroll, 1 );

		Layout.Add( WeaponAnimatorTheme.Button(
			"Confirm weapon bones",
			"verified",
			ConfirmWeaponBones,
			this,
			true ) );

		var reset = WeaponAnimatorTheme.Button(
			"Reset test pose",
			"restart_alt",
			() => ResetTestPoseRequested?.Invoke(),
			this );
		Layout.Add( reset );

		_controller.DocumentChanged += Refresh;
		_controller.SelectionChanged += RebuildBones;
		Refresh();
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Refresh;
		_controller.SelectionChanged -= RebuildBones;
		base.OnDestroyed();
	}

	public void Refresh()
	{
		var source = _controller.Document.Source;
		_sourceStatus.Text = string.IsNullOrWhiteSpace( source.SourcePath )
			? "No source selected"
			: $"{source.SourcePath}\n{_controller.Document.Rig.Bones.Count} bones · "
				+ $"{source.Materials.Count( material => material.HasUsableTextures )}/"
				+ $"{source.Materials.Count} textured materials · "
				+ $"{(source.Compiled ? "compiled" : "compile failed")}";
		var rig = _controller.Document.Rig;
		var retained = rig.Bones.Count( WeaponRigHierarchy.IsRetained );
		var structural = rig.Bones.Count( x =>
			x.Inclusion == WeaponBoneInclusion.StructuralBridge
			|| x.Classification == WeaponBoneClassification.Structural );
		var excluded = rig.Bones.Count - retained;
		_retainedStatus.Text = rig.Bones.Count == 0
			? "No weapon subtree selected"
			: $"{retained} retained · {structural} structural · {excluded} excluded"
				+ (rig.ReviewRequired ? "\nReview and confirm the filtered weapon preview." : "\nWeapon bones confirmed.")
				+ (rig.Bones.Any( x =>
						x.Inclusion == WeaponBoneInclusion.Excluded && x.HasSkinInfluence )
					? "\nExcluded branches may affect visible geometry; verify the model before confirming."
					: "");
		RebuildBones();
	}

	private void RebuildBones()
	{
		if ( _boneCanvas is null )
			return;
		_boneCanvas.Layout.Clear( true );

		foreach ( var bone in _controller.Document.Rig.Bones.Where( IsVisible ) )
		{
			var row = Row( _boneCanvas );
			row.FixedHeight = 28;
			var depth = BoneDepth( bone );
			var name = new WeaponAnimatorButton( $"{new string( ' ', Math.Min( depth, 8 ) * 2 )}{bone.Name}", row )
			{
				Clicked = () => _controller.SelectBone( bone.Name ),
				Tint = bone.Name == _controller.Document.Workspace.SelectedBone
					? WeaponAnimatorTheme.Cyan * 0.45f
					: WeaponAnimatorTheme.Surface
			};
			row.Layout.Add( name, 1 );

			var classification = new WeaponAnimatorButton( ShortClassification( bone.Classification ), row )
			{
				Clicked = () => CycleClassification( bone ),
				FixedWidth = 34,
				ToolTip = $"{bone.Classification} · {bone.Inclusion}",
				Tint = bone.Classification switch
				{
					WeaponBoneClassification.WeaponRoot => WeaponAnimatorTheme.Coral * 0.55f,
					WeaponBoneClassification.Animatable => WeaponAnimatorTheme.Amber * 0.45f,
					WeaponBoneClassification.Structural => WeaponAnimatorTheme.SurfaceRaised,
					_ => WeaponAnimatorTheme.Background
				}
			};
			row.Layout.Add( classification );
			_boneCanvas.Layout.Add( row );
		}

		_boneCanvas.Layout.AddStretchCell();
	}

	private bool IsVisible( WeaponBoneDefinition bone )
	{
		if ( !string.IsNullOrWhiteSpace( _filter )
			&& !bone.Name.Contains( _filter, StringComparison.OrdinalIgnoreCase ) )
			return false;

		return bone.Classification switch
		{
			WeaponBoneClassification.WeaponRoot => _showMovable,
			WeaponBoneClassification.Animatable => _showMovable,
			WeaponBoneClassification.Structural => _showStructural,
			WeaponBoneClassification.Ignored => _showIgnored,
			_ => true
		};
	}

	private void CycleClassification( WeaponBoneDefinition selected )
	{
		if ( selected.Inclusion == WeaponBoneInclusion.Excluded
			|| selected.Classification == WeaponBoneClassification.WeaponRoot )
			return;

		var next = selected.Classification switch
		{
			WeaponBoneClassification.Animatable => WeaponBoneClassification.Structural,
			_ => WeaponBoneClassification.Animatable
		};

		_controller.Mutate( $"Classify {selected.Name}", document =>
		{
			selected.Classification = next;
			document.Rig.ReviewRequired = true;
			document.Rig.FilteredPreviewConfirmed = false;
			document.Source.PreviewHostCompiled = false;
			document.Calibration.Confirmed = false;
		} );
	}

	private void SetSelectedWeaponRoot()
	{
		var selected = _controller.Document.Workspace.SelectedBone;
		if ( string.IsNullOrWhiteSpace( selected ) )
			return;
		_controller.Mutate( $"Set weapon root {selected}", document =>
		{
			WeaponRigHierarchy.SelectWeaponSubtree( document.Rig, selected );
			document.Source.PreviewHostCompiled = false;
			document.Calibration.Confirmed = false;
		} );
	}

	private void ExcludeSelectedBranch()
	{
		var selected = _controller.Document.Workspace.SelectedBone;
		if ( string.IsNullOrWhiteSpace( selected ) )
			return;
		_controller.Mutate( $"Exclude branch {selected}", document =>
		{
			if ( !WeaponRigHierarchy.ExcludeBranch( document.Rig, selected ) )
				return;
			document.Source.PreviewHostCompiled = false;
			document.Calibration.Confirmed = false;
		} );
	}

	private void IncludeSelectedBranch()
	{
		var selected = _controller.Document.Workspace.SelectedBone;
		if ( string.IsNullOrWhiteSpace( selected ) )
			return;
		_controller.Mutate( $"Include branch {selected}", document =>
		{
			if ( !WeaponRigHierarchy.IncludeBranch( document.Rig, selected ) )
				return;
			document.Source.PreviewHostCompiled = false;
			document.Calibration.Confirmed = false;
		} );
	}

	private void ConfirmWeaponBones()
	{
		if ( _controller.Document.Rig.Bones.Count == 0 )
			return;
		_controller.Mutate( "Confirm filtered weapon bones", document =>
		{
			WeaponRigHierarchy.ConfirmFilteredPreview( document.Rig );
			document.Rig.ProfileHash = WeaponSourceImporter.HashText(
				WeaponRigHierarchy.ProfileText( document.Rig ) );
			document.Source.PreviewHostCompiled = false;
			document.Calibration.Confirmed = false;
		} );
		RigReviewConfirmed?.Invoke();
	}

	private int BoneDepth( WeaponBoneDefinition bone )
	{
		var depth = 0;
		var parent = bone.ParentName;
		while ( !string.IsNullOrWhiteSpace( parent ) && depth < 16 )
		{
			depth++;
			parent = _controller.Document.Rig.FindBone( parent )?.ParentName ?? "";
		}
		return depth;
	}

	private static string ShortClassification( WeaponBoneClassification value ) => value switch
	{
		WeaponBoneClassification.WeaponRoot => "R",
		WeaponBoneClassification.Animatable => "A",
		WeaponBoneClassification.Structural => "S",
		_ => "×"
	};

	private Button Toggle( Widget parent, string text, bool value, Action<bool> changed )
	{
		var button = new WeaponAnimatorButton( text, parent )
		{
			IsToggle = true,
			IsChecked = value,
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		button.Toggled = () =>
		{
			changed( button.IsChecked );
			RebuildBones();
		};
		return button;
	}

	internal static Widget Row( Widget parent )
	{
		var row = new Widget( parent );
		row.SetStyles( "background-color: transparent; border: none;" );
		row.Layout = Layout.Row();
		row.Layout.Margin = 0;
		row.Layout.Spacing = 4;
		return row;
	}
}

public sealed class CalibrationInspectorPanel : Widget
{
	private readonly WeaponAnimatorController _controller;
	private readonly Label _selectedBone;
	private readonly Label _measurementResult;
	private readonly Label _alignmentResult;
	private readonly Label _confirmationState;
	private readonly LineEdit _knownDistance;
	private readonly List<Action> _transformRefreshers = [];
	private Vector3 _modelDimensions;

	public event Action<ViewportPickMode>? PickRequested;
	public event Action? AutoAlignRequested;
	public event Action? ConfirmRequested;
	public event Action? RebuildPreviewRequested;

	public CalibrationInspectorPanel(
		WeaponAnimatorController controller,
		Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		var scroll = new ScrollArea( this );
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Add( scroll, 1 );

		var canvas = new Widget( scroll );
		canvas.Layout = Layout.Column();
		canvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin( 10 );
		canvas.Layout.Spacing = 8;
		scroll.Canvas = canvas;

		canvas.Layout.Add( Section( canvas, "SELECTION" ) );
		_selectedBone = WeaponAnimatorTheme.Label( "No bone selected", canvas, true );
		canvas.Layout.Add( _selectedBone );

		canvas.Layout.Add( Section( canvas, "PHYSICAL MEASUREMENT" ) );
		var pickRow = RigAuditPanel.Row( canvas );
		pickRow.Layout.Add( WeaponAnimatorTheme.Button(
			"Point A",
			"looks_one",
			() => PickRequested?.Invoke( ViewportPickMode.MeasurementFirst ),
			pickRow ), 1 );
		pickRow.Layout.Add( WeaponAnimatorTheme.Button(
			"Point B",
			"looks_two",
			() => PickRequested?.Invoke( ViewportPickMode.MeasurementSecond ),
			pickRow ), 1 );
		canvas.Layout.Add( pickRow );

		var knownRow = RigAuditPanel.Row( canvas );
		_knownDistance = new LineEdit( knownRow )
		{
			PlaceholderText = "Known distance",
			FixedHeight = 28
		};
		_knownDistance.SetStyles( WeaponAnimatorTheme.InputStyle );
		_knownDistance.EditingFinished += PreviewScale;
		knownRow.Layout.Add( _knownDistance, 1 );

		var unitButton = new WeaponAnimatorButton( "in", knownRow )
		{
			FixedWidth = 48,
			Clicked = ToggleUnit,
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		knownRow.Layout.Add( unitButton );
		canvas.Layout.Add( knownRow );

		_measurementResult = WeaponAnimatorTheme.Label( "Pick two points to establish scale.", canvas, true );
		_measurementResult.WordWrap = true;
		canvas.Layout.Add( _measurementResult );
		canvas.Layout.Add( WeaponAnimatorTheme.Button(
			"Apply scale",
			"done",
			ApplyScale,
			canvas,
			true ) );

		canvas.Layout.Add( Section( canvas, "AUTO-ALIGN MARKERS · OPTIONAL" ) );
		var alignmentNote = WeaponAnimatorTheme.Label(
			"Only needed when Auto-align should rotate an incorrectly oriented source model.",
			canvas,
			true );
		alignmentNote.WordWrap = true;
		canvas.Layout.Add( alignmentNote );
		AddPickButton( canvas, "Alignment marker — rear", "radio_button_unchecked", ViewportPickMode.RearBoreAnchor );
		AddPickButton( canvas, "Alignment marker — front", "adjust", ViewportPickMode.FrontBoreAnchor );
		var autoAlign = WeaponAnimatorTheme.Button(
			"Auto-align from markers",
			"center_focus_strong",
			() => AutoAlignRequested?.Invoke(),
			canvas,
			true );
		autoAlign.ToolTip = "Uses the rear-to-front marker direction to rotate and place the weapon";
		canvas.Layout.Add( autoAlign );
		_alignmentResult = WeaponAnimatorTheme.Label(
			"Skip these markers when the source orientation is already correct.",
			canvas,
			true );
		_alignmentResult.WordWrap = true;
		canvas.Layout.Add( _alignmentResult );

		canvas.Layout.Add( Section( canvas, "GRIP ANCHOR · REQUIRED" ) );
		var gripNote = WeaponAnimatorTheme.Label(
			"Seeds the default primary-hand target used on the animation page.",
			canvas,
			true );
		gripNote.WordWrap = true;
		canvas.Layout.Add( gripNote );
		AddPickButton( canvas, "Primary grip", "pan_tool", ViewportPickMode.GripAnchor );

		canvas.Layout.Add( Section( canvas, "OUTPUT ANCHORS · OPTIONAL" ) );
		AddPickButton( canvas, "Muzzle", "flare", ViewportPickMode.MuzzleAnchor );
		AddPickButton( canvas, "Eject", "outbound", ViewportPickMode.EjectAnchor );
		canvas.Layout.Add( WeaponAnimatorTheme.Button(
			"Clear all anchors",
			"delete_sweep",
			ClearAllAnchors,
			canvas ) );
		_transformRefreshers.Add( () =>
		{
			var calibration = _controller.Document.Calibration;
			autoAlign.Enabled = calibration.GetAnchor( AnchorKind.Grip ) is not null
				&& calibration.GetAnchor( AnchorKind.RearBore ) is not null
				&& calibration.GetAnchor( AnchorKind.FrontBore ) is not null;
		} );

		canvas.Layout.Add( Section( canvas, "NUMERIC TRANSFORMS" ) );
		AddTransformFields( canvas, "Physical", false );
		AddTransformFields( canvas, "Viewmodel framing", true );

		canvas.Layout.Add( Section( canvas, "PREVIEW" ) );
		var previewNote = WeaponAnimatorTheme.Label(
			"Viewmodel camera is optional. It previews a fixed origin and does not author the player's camera.",
			canvas,
			true );
		previewNote.WordWrap = true;
		canvas.Layout.Add( previewNote );
		var modeRow = RigAuditPanel.Row( canvas );
		modeRow.Layout.Add( Toggle(
			modeRow,
			"Viewmodel camera",
			_controller.Document.Workspace.FirstPersonPreview,
			value => _controller.Mutate( "Preview mode", d => d.Workspace.FirstPersonPreview = value ) ), 1 );
		modeRow.Layout.Add( Toggle(
			modeRow,
			"Safe area",
			_controller.Document.Calibration.ShowSafeArea,
			value => _controller.Mutate( "Safe area", d => d.Calibration.ShowSafeArea = value ) ), 1 );
		canvas.Layout.Add( modeRow );
		canvas.Layout.Add( ChoiceButton(
			canvas,
			"Aspect",
			() => _controller.Document.Calibration.AspectGuide,
			["4:3", "16:9", "21:9"],
			value => _controller.Mutate( "Aspect guide", d => d.Calibration.AspectGuide = value ) ) );
		canvas.Layout.Add( ChoiceButton(
			canvas,
			"Up axis",
			() => _controller.Document.Calibration.UpAxis.ToString(),
			Enum.GetNames<WeaponUpAxis>(),
			value => _controller.Mutate(
				"Alignment up axis",
				d => d.Calibration.UpAxis = Enum.Parse<WeaponUpAxis>( value ) ) ) );
		canvas.Layout.Add( WeaponAnimatorTheme.Button(
			"Rebuild preview host",
			"refresh",
			() => RebuildPreviewRequested?.Invoke(),
			canvas ) );

		canvas.Layout.Add( Section( canvas, "CALIBRATION GATE" ) );
		_confirmationState = WeaponAnimatorTheme.Label( "", canvas, true );
		_confirmationState.WordWrap = true;
		canvas.Layout.Add( _confirmationState );
		canvas.Layout.Add( WeaponAnimatorTheme.Button(
			"Confirm rig and continue",
			"arrow_forward",
			() => ConfirmRequested?.Invoke(),
			canvas,
			true ) );
		canvas.Layout.AddStretchCell();

		_controller.DocumentChanged += Refresh;
		_controller.SelectionChanged += Refresh;
		Refresh();
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Refresh;
		_controller.SelectionChanged -= Refresh;
		base.OnDestroyed();
	}

	public void SetModelDimensions( Vector3 dimensions )
	{
		_modelDimensions = dimensions;
		Refresh();
	}

	public void SetAlignmentMessage( string message )
	{
		_alignmentResult.Text = message;
	}

	private void Refresh()
	{
		var document = _controller.Document;
		if ( CalibrationSelection.TryGetAnchor( document.Workspace.SelectedControl, out var anchorKind ) )
		{
			var anchor = document.Calibration.GetAnchor( anchorKind );
			_selectedBone.Text = anchor is null
				? "Anchor not set"
				: $"{CalibrationSelection.DisplayName( anchorKind )} · use Move or Rotate in the viewport";
		}
		else
		{
			_selectedBone.Text = string.IsNullOrWhiteSpace( document.Workspace.SelectedBone )
				? "No control selected"
				: $"{document.Workspace.SelectedBone} bone";
		}

		var measurement = document.Calibration.Measurement;
		_knownDistance.Value = measurement.KnownDistance > 0
			? measurement.KnownDistance.ToString( "0.###", CultureInfo.InvariantCulture )
			: "";
		PreviewScale();

		var report = WeaponAnimationValidator.ValidateCalibration( document );
		_confirmationState.Text = report.IsValid
			? "All calibration checks pass. Confirmation will snapshot the rig and seed Idle."
			: string.Join( "\n", report.Issues.Where( x => x.Blocking ).Take( 5 ).Select( x => $"• {x.Message}" ) );
		foreach ( var refresh in _transformRefreshers )
			refresh();
	}

	private void PreviewScale()
	{
		if ( !float.TryParse(
			_knownDistance.Text,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var known ) )
		{
			return;
		}

		var measurement = _controller.Document.Calibration.Measurement;
		measurement.KnownDistance = known;
		if ( !measurement.HasFirstPoint || !measurement.HasSecondPoint
			|| !WeaponAnimationMath.TryCalculateUniformScale(
				measurement.FirstPoint,
				measurement.SecondPoint,
				known,
				measurement.Unit,
				_modelDimensions,
				out var preview ) )
		{
			measurement.HasPendingScale = false;
			_measurementResult.Text = "Pick two distinct points and enter a positive known distance.";
			return;
		}

		measurement.PreviewScale = preview.UniformScale;
		measurement.HasPendingScale = true;
		measurement.OriginalDimensions = preview.OriginalDimensions;
		measurement.ResultingDimensions = preview.ResultingDimensions;
		_measurementResult.Text =
			$"Measured {preview.MeasuredUnits:0.###} units. Scale ×{preview.UniformScale:0.####}\n" +
			$"Original XYZ: {FormatDimensions( preview.OriginalDimensions )}\n" +
			$"Result XYZ: {FormatDimensions( preview.ResultingDimensions )}";
	}

	private void ApplyScale()
	{
		PreviewScale();
		var measurement = _controller.Document.Calibration.Measurement;
		if ( !measurement.HasPendingScale )
			return;

		_controller.Mutate( "Apply uniform scale", document =>
		{
			document.Calibration.UniformScale = measurement.PreviewScale;
			document.Calibration.PhysicalTransform =
				document.Calibration.PhysicalTransform.WithScale( measurement.PreviewScale );
			document.Calibration.Confirmed = false;
			measurement.HasPendingScale = false;
		} );
	}

	private void AddTransformFields( Widget parent, string label, bool framing )
	{
		parent.Layout.Add( WeaponAnimatorTheme.Label( label, parent, true ) );
		AddVectorField(
			parent,
			"Position",
			() => GetCalibrationTransform( framing ).Position,
			0.05f,
			(document, value) =>
				SetCalibrationTransform(
					document,
					framing,
					GetCalibrationTransform( document, framing ).WithPosition( value ) ) );
		AddVectorField(
			parent,
			"Rotation",
			() =>
			{
				var angles = GetCalibrationTransform( framing ).Rotation.Angles();
				return new Vector3( angles.pitch, angles.yaw, angles.roll );
			},
			0.5f,
			(document, value) =>
				SetCalibrationTransform(
					document,
					framing,
					GetCalibrationTransform( document, framing )
						.WithRotation( Rotation.From( value.x, value.y, value.z ) ) ) );
		AddScalarField(
			parent,
			"Scale",
			() => GetCalibrationTransform( framing ).Scale.x,
			0.005f,
			(document, value) =>
			{
				var clamped = MathF.Max( value, 0.0001f );
				SetCalibrationTransform(
					document,
					framing,
					GetCalibrationTransform( document, framing ).WithScale( clamped ) );
				if ( !framing )
					document.Calibration.UniformScale = clamped;
			} );
	}

	private void AddVectorField(
		Widget parent,
		string label,
		Func<Vector3> getter,
		float sensitivity,
		Action<WeaponAnimationDocument, Vector3> apply )
	{
		var row = RigAuditPanel.Row( parent );
		row.Layout.Add( WeaponAnimatorTheme.Label( label, row, true ), 1 );
		var edits = new LineEdit[3];
		var axisNames = new[] { "X", "Y", "Z" };
		var axisColors = new[]
		{
			WeaponAnimatorTheme.Coral,
			WeaponAnimatorTheme.Green,
			new Color( 0.30f, 0.56f, 0.96f )
		};
		for ( var index = 0; index < edits.Length; index++ )
		{
			var capturedIndex = index;
			var field = new Widget( row )
			{
				FixedWidth = 68,
				FixedHeight = 26,
				Layout = Layout.Row()
			};
			field.Layout.Margin = 0;
			field.Layout.Spacing = 0;
			var edit = new LineEdit( field ) { FixedWidth = 48, FixedHeight = 26 };
			edit.SetStyles( WeaponAnimatorTheme.InputStyle );
			edit.EditingFinished += () =>
			{
				var current = getter();
				if ( !float.TryParse(
					edit.Text,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var parsed ) )
					return;
				current[capturedIndex] = parsed;
				_controller.Mutate(
					$"{label} {axisNames[capturedIndex]}",
					document => apply( document, current ) );
			};
			field.Layout.Add( new ScrubHandle(
				axisNames[capturedIndex],
				axisColors[capturedIndex],
				sensitivity,
				() => getter()[capturedIndex],
				() => _controller.BeginContinuousEdit( $"{label} {axisNames[capturedIndex]}" ),
				value =>
				{
					var current = getter();
					current[capturedIndex] = value;
					_controller.UpdateContinuousEdit( document => apply( document, current ) );
				},
				_controller.EndContinuousEdit,
				field ) );
			field.Layout.Add( edit );
			edits[index] = edit;
			row.Layout.Add( field );
		}
		_transformRefreshers.Add( () =>
		{
			var value = getter();
			for ( var index = 0; index < edits.Length; index++ )
				edits[index].Value = value[index].ToString( "0.###", CultureInfo.InvariantCulture );
		} );
		parent.Layout.Add( row );
	}

	private void AddScalarField(
		Widget parent,
		string label,
		Func<float> getter,
		float sensitivity,
		Action<WeaponAnimationDocument, float> apply )
	{
		var row = RigAuditPanel.Row( parent );
		row.Layout.Add( WeaponAnimatorTheme.Label( label, row, true ), 1 );
		var field = new Widget( row )
		{
			FixedWidth = 104,
			FixedHeight = 26,
			Layout = Layout.Row()
		};
		field.Layout.Margin = 0;
		field.Layout.Spacing = 0;
		var edit = new LineEdit( field ) { FixedWidth = 72, FixedHeight = 26 };
		edit.SetStyles( WeaponAnimatorTheme.InputStyle );
		edit.EditingFinished += () =>
		{
			if ( float.TryParse( edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed ) )
				_controller.Mutate( label, document => apply( document, parsed ) );
		};
		field.Layout.Add( new ScrubHandle(
			"XYZ",
			WeaponAnimatorTheme.Amber,
			sensitivity,
			getter,
			() => _controller.BeginContinuousEdit( label ),
			value => _controller.UpdateContinuousEdit( document => apply( document, value ) ),
			_controller.EndContinuousEdit,
			field )
		{
			FixedWidth = 32
		} );
		field.Layout.Add( edit );
		row.Layout.Add( field );
		_transformRefreshers.Add( () =>
			edit.Value = getter().ToString( "0.###", CultureInfo.InvariantCulture ) );
		parent.Layout.Add( row );
	}

	private Transform GetCalibrationTransform( bool framing ) =>
		GetCalibrationTransform( _controller.Document, framing );

	private static Transform GetCalibrationTransform( WeaponAnimationDocument document, bool framing ) =>
		framing ? document.Calibration.FramingTransform : document.Calibration.PhysicalTransform;

	private static void SetCalibrationTransform(
		WeaponAnimationDocument document,
		bool framing,
		Transform value )
	{
		if ( framing )
			document.Calibration.FramingTransform = value;
		else
		{
			document.Calibration.PhysicalTransform = value;
			document.Calibration.Confirmed = false;
		}
	}

	private static string FormatDimensions( Vector3 dimensions )
	{
		var centimetres = dimensions * WeaponAnimationMath.CentimetresPerInch;
		return $"{dimensions.x:0.##}×{dimensions.y:0.##}×{dimensions.z:0.##} in · " +
			$"{centimetres.x:0.##}×{centimetres.y:0.##}×{centimetres.z:0.##} cm";
	}

	private void ToggleUnit()
	{
		_controller.Mutate( "Measurement unit", document =>
		{
			var measurement = document.Calibration.Measurement;
			if ( measurement.Unit == MeasurementUnit.Inches )
			{
				measurement.Unit = MeasurementUnit.Centimetres;
				measurement.KnownDistance *= WeaponAnimationMath.CentimetresPerInch;
			}
			else
			{
				measurement.Unit = MeasurementUnit.Inches;
				measurement.KnownDistance /= WeaponAnimationMath.CentimetresPerInch;
			}
		} );
	}

	private void AddPickButton( Widget parent, string name, string icon, ViewportPickMode mode )
	{
		var kind = mode switch
		{
			ViewportPickMode.GripAnchor => AnchorKind.Grip,
			ViewportPickMode.RearBoreAnchor => AnchorKind.RearBore,
			ViewportPickMode.FrontBoreAnchor => AnchorKind.FrontBore,
			ViewportPickMode.MuzzleAnchor => AnchorKind.Muzzle,
			ViewportPickMode.EjectAnchor => AnchorKind.Eject,
			_ => AnchorKind.Custom
		};
		var row = RigAuditPanel.Row( parent );
		var select = WeaponAnimatorTheme.Button(
			name,
			icon,
			() =>
			{
				if ( _controller.Document.Calibration.GetAnchor( kind ) is null )
					PickRequested?.Invoke( mode );
				else
					_controller.SelectControl( CalibrationSelection.Anchor( kind ) );
			},
			row );
		row.Layout.Add( select, 1 );
		var repick = WeaponAnimatorTheme.Button(
			"",
			"my_location",
			() => PickRequested?.Invoke( mode ),
			row );
		repick.FixedWidth = 34;
		repick.ToolTip = $"Pick {name.ToLowerInvariant()} again";
		row.Layout.Add( repick );
		var delete = WeaponAnimatorTheme.Button(
			"",
			"delete",
			() => DeleteAnchor( kind ),
			row );
		delete.FixedWidth = 34;
		delete.ToolTip = $"Delete {name.ToLowerInvariant()}";
		row.Layout.Add( delete );
		_transformRefreshers.Add( () =>
		{
			var exists = _controller.Document.Calibration.GetAnchor( kind ) is not null;
			var selected = _controller.Document.Workspace.SelectedControl == CalibrationSelection.Anchor( kind );
			select.Text = exists ? $"Move {name}" : $"Set {name}";
			select.Tint = selected
				? WeaponAnimatorTheme.Cyan * 0.48f
				: WeaponAnimatorTheme.SurfaceRaised;
			repick.Enabled = exists;
			delete.Enabled = exists;
		} );
		parent.Layout.Add( row );
	}

	private void DeleteAnchor( AnchorKind kind )
	{
		if ( _controller.Document.Calibration.GetAnchor( kind ) is null )
			return;

		_controller.Mutate( $"Delete {CalibrationSelection.DisplayName( kind )} anchor", document =>
		{
			document.Calibration.Anchors.RemoveAll( anchor => anchor.Kind == kind );
			if ( document.Workspace.SelectedControl == CalibrationSelection.Anchor( kind ) )
				document.Workspace.SelectedControl = "";
			document.Calibration.Confirmed = false;
		} );
	}

	private void ClearAllAnchors()
	{
		if ( _controller.Document.Calibration.Anchors.Count == 0 )
			return;

		_controller.Mutate( "Clear all anchors", document =>
		{
			document.Calibration.Anchors.Clear();
			if ( CalibrationSelection.TryGetAnchor( document.Workspace.SelectedControl, out _ ) )
				document.Workspace.SelectedControl = "";
			document.Calibration.Confirmed = false;
		} );
	}

	private static Label Section( Widget parent, string text )
	{
		return WeaponAnimatorTheme.SectionLabel( text, parent, topMargin: true );
	}

	private static Button Toggle( Widget parent, string text, bool value, Action<bool> changed )
	{
		var button = new WeaponAnimatorButton( text, parent )
		{
			IsToggle = true,
			IsChecked = value,
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		button.Toggled = () => changed( button.IsChecked );
		return button;
	}

	private static Button ChoiceButton(
		Widget parent,
		string label,
		Func<string> current,
		System.Collections.Generic.IEnumerable<string> values,
		Action<string> changed )
	{
		var button = new WeaponAnimatorButton( $"{label}: {current()}", "expand_more", parent )
		{
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		button.Clicked = () =>
		{
			var menu = new Menu( button );
			foreach ( var value in values )
			{
				var captured = value;
				menu.AddOption( captured, null, () =>
				{
					changed( captured );
					button.Text = $"{label}: {current()}";
					button.FitToContent();
				} );
			}
			menu.OpenAt( button.ScreenRect.BottomLeft );
		};
		return button;
	}
}

public sealed class ValidationStatusPanel : Widget
{
	private readonly Label _label;

	public ValidationStatusPanel( Widget? parent = null ) : base( parent )
	{
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 12, 8, 12, 8 );
		_label = WeaponAnimatorTheme.Label( "Ready", this, true );
		_label.WordWrap = true;
		Layout.Add( _label, 1 );
	}

	public void SetReport( ValidationReport report, string prefix = "" )
	{
		var status = report.IsValid
			? report.WarningCount == 0 ? "READY" : $"{report.WarningCount} WARNING(S)"
			: $"{report.ErrorCount} ERROR(S)";
		_label.Color = report.IsValid
			? report.WarningCount == 0 ? WeaponAnimatorTheme.Green : WeaponAnimatorTheme.Amber
			: WeaponAnimatorTheme.Coral;
		_label.Text = string.IsNullOrWhiteSpace( prefix )
			? $"{status} · {string.Join( "  ·  ", report.Issues.Take( 4 ).Select( x => x.Message ) )}"
			: $"{prefix} · {status} · {string.Join( "  ·  ", report.Issues.Take( 4 ).Select( x => x.Message ) )}";
	}

	public void SetMessage( string message, ValidationSeverity severity = ValidationSeverity.Info )
	{
		_label.Text = message;
		_label.Color = severity switch
		{
			ValidationSeverity.Error => WeaponAnimatorTheme.Coral,
			ValidationSeverity.Warning => WeaponAnimatorTheme.Amber,
			_ => WeaponAnimatorTheme.Muted
		};
	}
}
