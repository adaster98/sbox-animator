#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class ClipRackPanel : Widget
{
	private readonly WeaponAnimatorController _controller;
	private readonly ScrollArea _clipScroll;
	private readonly Widget _clipCanvas;
	private readonly ScrollArea _propertiesScroll;
	private readonly Widget _propertiesCanvas;
	private readonly Label _actionHint;
	private readonly Dictionary<Guid, WeaponAnimatorButton> _clipButtons = [];
	private readonly Dictionary<Guid, int> _propertyScrollByClip = [];
	private string _clipListSignature = "";
	private Guid _lastSelectedClipId;

	public event Action<string, ValidationSeverity>? StatusChanged;

	public ClipRackPanel(
		WeaponAnimatorController controller,
		Widget? parent = null,
		bool showClipHeader = true ) : base( parent )
	{
		_controller = controller;
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 8 );
		Layout.Spacing = 6;

		if ( showClipHeader )
			Layout.Add( Header( "CLIP RACK", this ) );
		_clipScroll = new ScrollArea( this )
		{
			MinimumSize = new Vector2( 200, 70 )
		};
		_clipCanvas = new Widget( _clipScroll );
		_clipCanvas.Layout = Layout.Column();
		_clipCanvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin();
		_clipCanvas.Layout.Spacing = 2;
		_clipScroll.Canvas = _clipCanvas;
		Layout.Add( _clipScroll, 2 );

		var actions = RigAuditPanel.Row( this );
		actions.Layout.Add( WeaponAnimatorTheme.Button(
			"Start",
			"add_circle",
			StartSelectedFromDefault,
			actions,
			true ), 1 );
		actions.Layout.Add( WeaponAnimatorTheme.Button(
			"Duplicate",
			"content_copy",
			ShowDuplicateMenu,
			actions ), 1 );
		actions.Layout.Add( WeaponAnimatorTheme.Button(
			"Import",
			"input",
			ShowImportMenu,
			actions ), 1 );
		Layout.Add( actions );

		_actionHint = WeaponAnimatorTheme.Label( "", this, true );
		_actionHint.WordWrap = true;
		Layout.Add( _actionHint );

		_propertiesScroll = new ScrollArea( this ) { MinimumHeight = 80 };
		_propertiesCanvas = new Widget( _propertiesScroll );
		_propertiesCanvas.Layout = Layout.Column();
		_propertiesCanvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin();
		_propertiesCanvas.Layout.Spacing = 4;
		_propertiesScroll.Canvas = _propertiesCanvas;
		Layout.Add( _propertiesScroll, 1 );

		var addCustom = WeaponAnimatorTheme.Button(
			"Add custom clip",
			"playlist_add",
			AddCustomClip,
			this );
		Layout.Add( addCustom );

		_controller.DocumentChanged += Rebuild;
		Rebuild();
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Rebuild;
		_controller.SelectionChanged -= Rebuild;
		base.OnDestroyed();
	}

	private void Rebuild()
	{
		var clipScroll = _clipScroll.VerticalScrollbar.Value;
		var selectedClipId = _controller.Document.Workspace.SelectedClipId;
		var propertiesScroll = CapturePropertiesScroll( selectedClipId );
		var clipSignature = ClipListSignature();
		if ( _clipListSignature != clipSignature || _clipButtons.Count == 0 )
		{
			_clipCanvas.Layout.Clear( true );
			_clipButtons.Clear();

			AddClipGroup( "CORE", [
				WeaponClipRole.Idle, WeaponClipRole.Deploy, WeaponClipRole.Fire,
				WeaponClipRole.FireDry, WeaponClipRole.Reload, WeaponClipRole.ReloadEmpty,
				WeaponClipRole.Holster
			] );
			AddClipGroup( "PRESENTATION", [
				WeaponClipRole.Inspect, WeaponClipRole.Sprint, WeaponClipRole.Jump,
				WeaponClipRole.Lower, WeaponClipRole.Ironsights
			] );
			AddClipGroup( "INTERACTION", [
				WeaponClipRole.GrabStance, WeaponClipRole.GrabGestureOne,
				WeaponClipRole.GrabGestureTwo, WeaponClipRole.GrabGestureThree,
				WeaponClipRole.GrabGestureFour
			] );
			AddClipGroup( "INCREMENTAL", [
				WeaponClipRole.ReloadEnter, WeaponClipRole.FirstShell,
				WeaponClipRole.InsertShell, WeaponClipRole.ReloadExit
			] );

			var custom = _controller.Document.Clips
				.Where( x => x.Role == WeaponClipRole.Custom )
				.ToArray();
			if ( custom.Length > 0 )
			{
				_clipCanvas.Layout.Add( Header( "CUSTOM", _clipCanvas ) );
				foreach ( var clip in custom )
					AddClipButton( clip );
			}
			_clipCanvas.Layout.AddStretchCell();
			_clipListSignature = ClipListSignature();
			_clipCanvas.UpdateGeometry();
			_clipScroll.VerticalScrollbar.Value = clipScroll;
		}
		else
		{
			RefreshClipButtons();
		}

		_propertiesCanvas.Layout.Clear( true );

		var selected = _controller.Document.GetSelectedClip();
		_actionHint.Text = selected is null
			? "Select a clip."
			: selected.Readiness == ClipReadiness.NotStarted
				? "Not started · choose Start, Duplicate, or Import."
				: $"{selected.Readiness} · {selected.Duration:0.###} s at {selected.SampleRate:0.#} fps";
		BuildClipProperties( selected );
		_propertiesCanvas.UpdateGeometry();
		_propertiesScroll.VerticalScrollbar.Value = propertiesScroll;
		_lastSelectedClipId = selectedClipId;
	}

	private void AddClipGroup( string name, IEnumerable<WeaponClipRole> roles )
	{
		_clipCanvas.Layout.Add( Header( name, _clipCanvas ) );
		foreach ( var role in roles )
		{
			var clip = _controller.Document.EnsureClip( role );
			AddClipButton( clip );
		}
	}

	private void BuildClipProperties( WeaponAnimationClip? clip )
	{
		if ( _propertiesCanvas is null || clip is null )
			return;
		_propertiesCanvas.Layout.Add( Header( "CLIP PROPERTIES", _propertiesCanvas ) );
		if ( clip.Role == WeaponClipRole.Custom )
			AddCustomClipProperties( clip );
		var sequence = WeaponAnimatorTheme.Label(
			$"Sequence: {WeaponAnimationNames.SequenceName( clip )}",
			_propertiesCanvas,
			true );
		sequence.ToolTip = "Generated sequence name";
		_propertiesCanvas.Layout.Add( sequence );
		AddClipNumber(
			"Duration",
			clip.Duration,
			value => _controller.Mutate( "Clip duration", _ =>
			{
				clip.Duration = MathF.Max( value, 1.0f / clip.SampleRate );
				clip.KeysClampToDuration();
			} ) );
		AddClipNumber(
			"Sample rate",
			clip.SampleRate,
			value => _controller.Mutate(
				"Clip sample rate",
				_ => clip.SampleRate = Math.Clamp( value, 1, 240 ) ) );
		_propertiesCanvas.Layout.Add( ClipChoice(
			$"Readiness: {clip.Readiness}",
			Enum.GetNames<ClipReadiness>(),
			value => _controller.Mutate(
				"Clip readiness",
				_ => clip.Readiness = Enum.Parse<ClipReadiness>( value ) ) ) );
		_propertiesCanvas.Layout.Add( ClipChoice(
			$"Interpolation: {DominantInterpolation( clip )}",
			Enum.GetNames<TrackInterpolation>(),
			value => _controller.Mutate( "Track interpolation", _ =>
			{
				var interpolation = Enum.Parse<TrackInterpolation>( value );
				foreach ( var track in clip.Tracks )
					track.Interpolation = interpolation;
			} ) ) );
		_propertiesCanvas.Layout.Add( Header( "TAGS", _propertiesCanvas ) );
		var tagRow = RigAuditPanel.Row( _propertiesCanvas );
		var name = new LineEdit( tagRow )
		{
			PlaceholderText = "Tag name",
			FixedHeight = 27
		};
		name.SetStyles( WeaponAnimatorTheme.InputStyle );
		tagRow.Layout.Add( name, 1 );
		tagRow.Layout.Add( WeaponAnimatorTheme.Button(
			"Point",
			"add_location",
			() => AddClipTag( name.Text, AnimationTagKind.Point ),
			tagRow ) );
		tagRow.Layout.Add( WeaponAnimatorTheme.Button(
			"Range",
			"linear_scale",
			() => AddClipTag( name.Text, AnimationTagKind.Range ),
			tagRow ) );
		_propertiesCanvas.Layout.Add( tagRow );
		foreach ( var tag in clip.Tags )
		{
			_propertiesCanvas.Layout.Add( WeaponAnimatorTheme.Label(
				$"{tag.Name}  {tag.StartTime:0.###}–{tag.EndTime:0.###}",
				_propertiesCanvas,
				true ) );
		}
		_propertiesCanvas.Layout.AddStretchCell();
	}

	private void AddCustomClipProperties( WeaponAnimationClip clip )
	{
		if ( _propertiesCanvas is null )
			return;

		var nameRow = RigAuditPanel.Row( _propertiesCanvas );
		nameRow.Layout.Add( WeaponAnimatorTheme.Label( "Name", nameRow, true ) );
		var name = new LineEdit( nameRow )
		{
			Text = clip.Name,
			FixedHeight = 26
		};
		name.SetStyles( WeaponAnimatorTheme.InputStyle );
		name.EditingFinished += () =>
		{
			var renamed = name.Text.Trim();
			if ( string.IsNullOrWhiteSpace( renamed ) )
			{
				name.Text = clip.Name;
				StatusChanged?.Invoke(
					"A custom clip name cannot be empty.",
					ValidationSeverity.Warning );
				return;
			}
			_controller.RenameCustomClip( clip.Id, renamed );
		};
		nameRow.Layout.Add( name, 1 );
		_propertiesCanvas.Layout.Add( nameRow );

		var delete = (WeaponAnimatorButton)WeaponAnimatorTheme.Button(
			"Delete custom clip",
			"delete",
			() => RequestDeleteCustomClip( clip ),
			_propertiesCanvas );
		delete.Tint = WeaponAnimatorTheme.Coral * 0.38f;
		_propertiesCanvas.Layout.Add( delete );
	}

	private void RequestDeleteCustomClip( WeaponAnimationClip clip )
	{
		Dialog.AskConfirm(
			() => _controller.DeleteCustomClip( clip.Id ),
			$"Delete the custom clip '{clip.Name}' and all of its keys, curves, tags, and visibility tracks?",
			"Delete Custom Clip",
			"Delete",
			"Cancel" );
	}

	private void AddClipNumber( string label, float value, Action<float> changed )
	{
		if ( _propertiesCanvas is null )
			return;
		var row = RigAuditPanel.Row( _propertiesCanvas );
		row.Layout.Add( WeaponAnimatorTheme.Label( label, row, true ), 1 );
		var edit = new LineEdit( row )
		{
			Text = value.ToString( "0.###", CultureInfo.InvariantCulture ),
			FixedWidth = 84,
			FixedHeight = 26
		};
		edit.SetStyles( WeaponAnimatorTheme.InputStyle );
		edit.EditingFinished += () =>
		{
			if ( float.TryParse( edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed )
				&& WeaponAnimationMath.IsFinite( parsed ) )
				changed( parsed );
		};
		row.Layout.Add( edit );
		_propertiesCanvas.Layout.Add( row );
	}

	private Button ClipChoice(
		string text,
		IEnumerable<string> values,
		Action<string> changed )
	{
		var button = new WeaponAnimatorButton( text, "expand_more", _propertiesCanvas )
		{
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		button.Clicked = () =>
		{
			var menu = new Menu( button );
			foreach ( var value in values )
			{
				var captured = value;
				menu.AddOption( captured, null, () => changed( captured ) );
			}
			menu.OpenAt( button.ScreenRect.BottomLeft );
		};
		return button;
	}

	private void AddClipTag( string name, AnimationTagKind kind )
	{
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null || string.IsNullOrWhiteSpace( name ) )
			return;
		_controller.Mutate( $"Add tag {name}", document =>
		{
			var start = document.Workspace.TimelineTime;
			clip.Tags.Add( new AnimationTag
			{
				Name = name.Trim(),
				Kind = kind,
				StartTime = start,
				EndTime = kind == AnimationTagKind.Range
					? MathF.Min( start + 0.1f, clip.Duration )
					: start
			} );
		} );
	}

	private static TrackInterpolation DominantInterpolation( WeaponAnimationClip clip ) =>
		clip.Tracks.GroupBy( x => x.Interpolation )
			.OrderByDescending( x => x.Count() )
			.Select( x => x.Key )
			.FirstOrDefault();

	private void AddClipButton( WeaponAnimationClip clip )
	{
		var button = new WeaponAnimatorButton( "", _clipCanvas )
		{
			Clicked = () => _controller.SelectClip( clip.Id )
		};
		ApplyClipButtonAppearance( button, clip );
		_clipCanvas.Layout.Add( button );
		_clipButtons[clip.Id] = button;
	}

	private void StartSelectedFromDefault()
	{
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null )
			return;

		_controller.Mutate( $"Start {clip.Name}", document =>
		{
			document.Workspace.ClearWorkingPoses( clip.Id );
			document.Workspace.TimelineViews.RemoveAll( x => x.ClipId == clip.Id );
			document.Workspace.CurveViews.RemoveAll( x => x.ClipId == clip.Id );
			clip.VisibilityTracks.Clear();
			var skeleton = HostSkeletonBuilder.BuildCached( document );
			if ( clip.Role == WeaponClipRole.Idle )
			{
				IdleBindPoseService.SeedFromCurrentBind( document, skeleton );
				return;
			}

			clip.Tracks.Clear();
			clip.IsBindPoseSeed = false;
			foreach ( var bone in skeleton.Bones )
			{
				var track = clip.EnsureTrack( bone.Name );
				track.Kind = bone.IsWeaponBone ? RigControlKind.Weapon : RigControlKind.Arm;
				var gripTransform = document.Binding.GripPoses
					.FirstOrDefault( x => x.Id == document.Binding.DefaultGripPoseId )?
					.Bones.FirstOrDefault( x => x.BoneName.Equals( bone.Name, StringComparison.OrdinalIgnoreCase ) )?
					.LocalTransform;
				WeaponAnimationMath.UpsertKey(
					track,
					0,
					gripTransform ?? skeleton.GetBindLocal( bone ) );
			}
			clip.Readiness = clip.Role == WeaponClipRole.Idle
				? ClipReadiness.Ready
				: ClipReadiness.Draft;
		} );
	}

	private void ShowDuplicateMenu()
	{
		var selected = _controller.Document.GetSelectedClip();
		if ( selected is null )
			return;
		var menu = new Menu( this );
		foreach ( var source in _controller.Document.Clips.Where( x =>
			x.Id != selected.Id && x.Readiness != ClipReadiness.NotStarted ) )
		{
			var captured = source;
			menu.AddOption( captured.Name, null, () => Duplicate( captured, selected ) );
		}
		menu.OpenAtCursor();
	}

	private void Duplicate( WeaponAnimationClip source, WeaponAnimationClip destination )
	{
		_controller.Mutate( $"Duplicate {source.Name}", _ =>
		{
			_controller.Document.Workspace.ClearWorkingPoses( destination.Id );
			_controller.Document.Workspace.TimelineViews.RemoveAll( x =>
				x.ClipId == destination.Id );
			_controller.Document.Workspace.CurveViews.RemoveAll( x =>
				x.ClipId == destination.Id );
			var copy = Json.Deserialize<WeaponAnimationClip>( Json.Serialize( source ) )!;
			destination.Duration = copy.Duration;
			destination.SampleRate = copy.SampleRate;
			destination.AllowSubframeKeys = copy.AllowSubframeKeys;
			destination.IsBindPoseSeed = false;
			destination.Tracks = copy.Tracks;
			destination.VisibilityTracks = copy.VisibilityTracks;
			destination.Constraints = copy.Constraints;
			destination.Tags = copy.Tags;
			destination.Readiness = ClipReadiness.Draft;
		} );
	}

	private void ShowImportMenu()
	{
		var selected = _controller.Document.GetSelectedClip();
		if ( selected is null )
			return;
		var sequences = SequenceImportService.GetSequences( _controller.Document );
		if ( sequences.Count == 0 )
		{
			StatusChanged?.Invoke( "The source model exposes no importable sequences.", ValidationSeverity.Warning );
			return;
		}

		var menu = new Menu( this );
		foreach ( var sequence in sequences )
		{
			var captured = sequence;
			menu.AddOption( captured, null, () =>
			{
				SequenceImportResult? result = null;
				_controller.Mutate( $"Import {captured}", document =>
				{
					document.Workspace.ClearWorkingPoses( selected.Id );
					document.Workspace.TimelineViews.RemoveAll( x =>
						x.ClipId == selected.Id );
					document.Workspace.CurveViews.RemoveAll( x =>
						x.ClipId == selected.Id );
					selected.IsBindPoseSeed = false;
					result = SequenceImportService.Import( document, selected, captured );
				} );
				StatusChanged?.Invoke(
					result?.Message ?? "Sequence import failed.",
					result?.Success == true ? ValidationSeverity.Info : ValidationSeverity.Error );
			} );
		}
		menu.OpenAtCursor();
	}

	private void AddCustomClip()
	{
		_controller.Mutate( "Add custom clip", document =>
		{
			var count = document.Clips.Count( x => x.Role == WeaponClipRole.Custom ) + 1;
			var clip = WeaponAnimationClip.Create( WeaponClipRole.Custom );
			clip.Name = $"Custom {count}";
			document.Clips.Add( clip );
			WeaponAnimationNames.RepairCustomSequenceNames( document );
			document.Workspace.SelectedClipId = clip.Id;
		} );
	}

	private static Label Header( string text, Widget parent )
	{
		var label = WeaponAnimatorTheme.SectionLabel( text, parent );
		label.FixedHeight = 22;
		label.SetStyles(
			"background-color: transparent; border: none; padding: 5px 0 0 0;" +
			$"font-size: 9px; font-weight: 600; letter-spacing: 0.65px; color: {WeaponAnimatorTheme.Muted.Hex};" );
		return label;
	}

	private int CapturePropertiesScroll( Guid selectedClipId )
	{
		if ( _propertiesScroll is null )
			return 0;

		if ( _lastSelectedClipId != Guid.Empty )
			_propertyScrollByClip[_lastSelectedClipId] =
				_propertiesScroll.VerticalScrollbar.Value;
		return _lastSelectedClipId == selectedClipId
			? _propertiesScroll.VerticalScrollbar.Value
			: _propertyScrollByClip.GetValueOrDefault( selectedClipId );
	}

	private string ClipListSignature() => string.Join(
		"|",
		_controller.Document.Clips.Select( x =>
			$"{x.Id}:{x.Role}:{x.Name}:{x.Readiness}" ) );

	private void RefreshClipButtons()
	{
		foreach ( var clip in _controller.Document.Clips )
		{
			if ( !_clipButtons.TryGetValue( clip.Id, out var button ) )
				continue;
			ApplyClipButtonAppearance( button, clip );
		}
	}

	private void ApplyClipButtonAppearance(
		WeaponAnimatorButton button,
		WeaponAnimationClip clip )
	{
		var marker = clip.Readiness switch
		{
			ClipReadiness.NotStarted => "○",
			ClipReadiness.Draft => "◐",
			ClipReadiness.Ready => "●",
			_ => "!"
		};
		button.Text = $"{marker}  {clip.Name}";
		button.Tint = clip.Id == _controller.Document.Workspace.SelectedClipId
			? WeaponAnimatorTheme.Cyan * 0.42f
			: clip.Readiness switch
			{
				ClipReadiness.Ready => WeaponAnimatorTheme.Green * 0.24f,
				ClipReadiness.Warning => WeaponAnimatorTheme.Coral * 0.28f,
				_ => WeaponAnimatorTheme.Surface
			};
		button.ToolTip = clip.Readiness.ToString();
	}

	internal ScrollArea ClipScroll => _clipScroll;
	internal ScrollArea? PropertiesScroll => _propertiesScroll;
	internal WeaponAnimatorButton? GetClipButton( Guid clipId ) =>
		_clipButtons.GetValueOrDefault( clipId );
}

public sealed class AnimationInspectorPanel : Widget
{
	private readonly WeaponAnimatorController _controller;
	private readonly Widget _canvas;
	private readonly bool _controlToolsOnly;
	private readonly Dictionary<string, bool> _expandedSections = new( StringComparer.OrdinalIgnoreCase )
	{
		["binding"] = true,
		["constraints"] = true,
		["animgraph"] = false
	};
	public event Action<string, ValidationSeverity>? StatusChanged;

	public AnimationInspectorPanel(
		WeaponAnimatorController controller,
		Widget? parent = null,
		bool controlToolsOnly = false ) : base( parent )
	{
		_controller = controller;
		_controlToolsOnly = controlToolsOnly;
		Layout = Layout.Column();
		Layout.Margin = 0;
		var scroll = new ScrollArea( this );
		_canvas = new Widget( scroll );
		_canvas.Layout = Layout.Column();
		_canvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin( 10 );
		_canvas.Layout.Spacing = 7;
		scroll.Canvas = _canvas;
		Layout.Add( scroll, 1 );

		_controller.DocumentChanged += Rebuild;
		_controller.SelectionChanged += Rebuild;
		Rebuild();
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Rebuild;
		_controller.SelectionChanged -= Rebuild;
		base.OnDestroyed();
	}

	private void Rebuild()
	{
		_canvas?.Layout.Clear( true );
		if ( _canvas is null )
			return;

		if ( !_controlToolsOnly )
		{
			_canvas.Layout.Add( Header( "CONTROL INSPECTOR" ) );
			_canvas.Layout.Add( WeaponAnimatorTheme.Label( SelectionName(), _canvas ) );
		}

		var bindingCanvas = _controlToolsOnly
			? AddCollapsibleSection( "BINDING + GRIP POSES", "binding" )
			: _canvas;
		var selectedControl = _controller.Document.Workspace.SelectedControl;
		if ( !string.IsNullOrWhiteSpace( selectedControl ) )
		{
			var selectedTarget = ResolveControl( selectedControl );
			if ( selectedTarget is not null
				&& selectedControl is "@primary_hand" or "@support_hand" )
			{
				var instruction = WeaponAnimatorTheme.Label(
					"Keep this hand selected. Choose its attachment bone from the menu below; "
					+ "you do not need to select the weapon bone in the rig browser.",
					bindingCanvas,
					true );
				instruction.WordWrap = true;
				bindingCanvas.Layout.Add( instruction );

				var weaponBones = HostSkeletonBuilder.BuildCached( _controller.Document )
					.Bones
					.Where( x => x.IsWeaponBone )
					.Select( x => x.Name )
					.Distinct( StringComparer.OrdinalIgnoreCase )
					.ToList();
				bindingCanvas.Layout.Add( ChoiceButton(
					"Attachment bone",
					() => string.IsNullOrWhiteSpace( selectedTarget.AttachedBone )
						? "weapon_root (recommended on bind)"
						: selectedTarget.AttachedBone,
					weaponBones.Prepend( "(world)" ),
					value => _controller.Mutate( "Change hand attachment", document =>
					{
						HandAttachmentService.ChangeAttachment(
							document,
							selectedControl,
							value == "(world)" ? "" : value );
					} ),
					bindingCanvas ) );

				bindingCanvas.Layout.Add( WeaponAnimatorTheme.Button(
					selectedTarget.IsBound ? $"Unbind {selectedTarget.Name}" : $"Bind {selectedTarget.Name}",
					selectedTarget.IsBound ? "link_off" : "link",
					() => ToggleHandBinding( selectedControl ),
					bindingCanvas,
					!selectedTarget.IsBound ) );
			}
		}

		var bindingRow = RigAuditPanel.Row( bindingCanvas );
		bindingRow.Layout.Add( WeaponAnimatorTheme.Button(
			_controller.Document.Binding.Configuration == GripConfiguration.TwoHanded
				? "Two handed"
				: "One handed",
			"pan_tool",
			ToggleGripConfiguration,
			bindingRow ), 1 );
		bindingRow.Layout.Add( WeaponAnimatorTheme.Button(
			"Save grip pose",
			"save",
			SaveGripPose,
			bindingRow,
			true ), 1 );
		bindingCanvas.Layout.Add( bindingRow );
		bindingCanvas.Layout.Add( WeaponAnimatorTheme.Button(
			"Apply saved grip pose",
			"front_hand",
			ShowGripPoseMenu,
			bindingCanvas ) );

		var clip = _controller.Document.GetSelectedClip();
		if ( clip is not null && !_controlToolsOnly )
		{
			_canvas.Layout.Add( Header( "CLIP PROPERTIES" ) );
			_canvas.Layout.Add( NumericField(
				"Duration (seconds)",
				clip.Duration,
				value => _controller.Mutate( "Clip duration", _ =>
				{
					clip.Duration = MathF.Max( value, 1.0f / clip.SampleRate );
					clip.KeysClampToDuration();
				} ) ) );
			_canvas.Layout.Add( NumericField(
				"Sample rate",
				clip.SampleRate,
				value => _controller.Mutate( "Clip sample rate", _ =>
					clip.SampleRate = Math.Clamp( value, 1, 240 ) ) ) );
			_canvas.Layout.Add( ChoiceButton(
				"Readiness",
				() => clip.Readiness.ToString(),
				Enum.GetNames<ClipReadiness>(),
				value => _controller.Mutate(
					"Clip readiness",
					_ => clip.Readiness = Enum.Parse<ClipReadiness>( value ) ) ) );
			_canvas.Layout.Add( ChoiceButton(
				"Interpolation",
				() => DominantInterpolation( clip ).ToString(),
				Enum.GetNames<TrackInterpolation>(),
				value => _controller.Mutate( "Track interpolation", _ =>
				{
					var interpolation = Enum.Parse<TrackInterpolation>( value );
					foreach ( var track in clip.Tracks )
						track.Interpolation = interpolation;
				} ) ) );
		}

		var constraintCanvas = _controlToolsOnly
			? AddCollapsibleSection( "CONSTRAINTS", "constraints" )
			: _canvas;
		if ( !_controlToolsOnly )
			constraintCanvas.Layout.Add( Header( "KEYING + CONSTRAINTS" ) );
		if ( !_controlToolsOnly )
		{
			var toggles = RigAuditPanel.Row( constraintCanvas );
			toggles.Layout.Add( ToggleButton(
				"Auto-key",
				_controller.Document.Workspace.AutoKey,
				value => _controller.Mutate( "Auto-key", d => d.Workspace.AutoKey = value ),
				toggles ), 1 );
			toggles.Layout.Add( ToggleButton(
				"Local gizmo",
				_controller.Document.Workspace.LocalGizmos,
				value => _controller.Mutate( "Gizmo space", d => d.Workspace.LocalGizmos = value ),
				toggles ), 1 );
			constraintCanvas.Layout.Add( toggles );
		}
		constraintCanvas.Layout.Add( WeaponAnimatorTheme.Button(
			"Constraint target",
			"target",
			ShowConstraintTargetMenu,
			constraintCanvas ) );
		constraintCanvas.Layout.Add( WeaponAnimatorTheme.Label(
			string.IsNullOrWhiteSpace( _controller.Document.Workspace.ConstraintTargetBone )
				? "No constraint target selected"
				: _controller.Document.Workspace.ConstraintTargetBone,
			constraintCanvas,
			true ) );
		constraintCanvas.Layout.Add( WeaponAnimatorTheme.Button(
			"Constrain selected control",
			"link",
			AddConstraint,
			constraintCanvas ) );

		if ( !_controlToolsOnly )
		{
			_canvas.Layout.Add( Header( "TAGS" ) );
			var tagRow = RigAuditPanel.Row( _canvas );
			var tagName = new LineEdit( tagRow )
			{
				PlaceholderText = "Tag name",
				FixedHeight = 28
			};
			tagName.SetStyles( WeaponAnimatorTheme.InputStyle );
			tagRow.Layout.Add( tagName, 1 );
			tagRow.Layout.Add( WeaponAnimatorTheme.Button(
				"Point",
				"add_location",
				() => AddTag( tagName.Text, AnimationTagKind.Point ),
				tagRow ) );
			tagRow.Layout.Add( WeaponAnimatorTheme.Button(
				"Range",
				"linear_scale",
				() => AddTag( tagName.Text, AnimationTagKind.Range ),
				tagRow ) );
			_canvas.Layout.Add( tagRow );

			if ( clip is not null )
			{
				foreach ( var tag in clip.Tags )
					_canvas.Layout.Add( WeaponAnimatorTheme.Label(
						$"{tag.Name}  {tag.StartTime:0.###}–{tag.EndTime:0.###}",
						_canvas,
						true ) );
			}
		}

		var graphCanvas = _controlToolsOnly
			? AddCollapsibleSection( "ANIMGRAPH PREVIEW", "animgraph" )
			: _canvas;
		if ( !_controlToolsOnly )
			graphCanvas.Layout.Add( Header( "ANIMGRAPH PREVIEW" ) );
		var graphActions = new[]
		{
			("Fire", "b_attack", WeaponClipRole.Fire),
			("Dry", "b_attack_dry", WeaponClipRole.FireDry),
			("Reload", "b_reload", WeaponClipRole.Reload),
			("Sprint", "b_sprint", WeaponClipRole.Sprint),
			("Inspect", "b_inspect", WeaponClipRole.Inspect)
		};
		var graphRows = new[]
		{
			RigAuditPanel.Row( graphCanvas ),
			RigAuditPanel.Row( graphCanvas )
		};
		for ( var index = 0; index < graphActions.Length; index++ )
		{
			var captured = graphActions[index];
			var row = graphRows[index < 3 ? 0 : 1];
			row.Layout.Add( WeaponAnimatorTheme.Button(
				captured.Item1,
				"play_arrow",
				() => SimulateParameter( captured.Item2, captured.Item3 ),
				row ), 1 );
		}
		graphCanvas.Layout.Add( graphRows[0] );
		graphCanvas.Layout.Add( graphRows[1] );
		graphCanvas.Layout.Add( NumericField(
			"move_bob",
			_controller.Document.Graph.PreviewFloats.GetValueOrDefault( "move_bob" ),
			value => _controller.Mutate( "Preview move_bob", d =>
				d.Graph.PreviewFloats["move_bob"] = Math.Clamp( value, 0, 1 ) ),
			graphCanvas ) );
		_canvas.Layout.AddStretchCell();
	}

	private Widget AddCollapsibleSection( string title, string id )
	{
		var expanded = _expandedSections.GetValueOrDefault( id );
		var header = new WeaponAnimatorButton(
			$"{(expanded ? "▾" : "▸")}  {title}",
			_canvas )
		{
			Clicked = () =>
			{
				_expandedSections[id] = !expanded;
				Rebuild();
			},
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		header.FixedHeight = 26;
		_canvas.Layout.Add( header );

		var body = new Widget( _canvas )
		{
			Visible = expanded,
			Layout = Layout.Column()
		};
		body.Layout.Margin = new Sandbox.UI.Margin( 2, 2, 2, 5 );
		body.Layout.Spacing = 6;
		_canvas.Layout.Add( body );
		return body;
	}

	private void ToggleHandBinding( string controlName )
	{
		var target = ResolveControl( controlName );
		if ( target is null )
			return;
		if ( !target.IsBound
			&& controlName == "@primary_hand"
			&& _controller.Document.Calibration.GetAnchor( AnchorKind.Grip ) is null )
		{
			StatusChanged?.Invoke(
				"Set the primary grip anchor in Calibrate before binding the primary hand.",
				ValidationSeverity.Warning );
			return;
		}

		_controller.Mutate(
			target.IsBound ? $"Unbind {target.Name}" : $"Bind {target.Name}",
			document =>
			{
				var bindingTarget = ResolveControl( controlName );
				if ( bindingTarget is null )
					return;

				if ( !bindingTarget.IsBound && controlName == "@primary_hand" )
					CalibrationBindingSeeder.SeedDefaultPrimaryHand( document );
				bindingTarget.IsBound = !bindingTarget.IsBound;
				bindingTarget.Reachable = true;

				var checklistId = controlName == "@primary_hand"
					? "primary_hand"
					: "support_hand";
				if ( bindingTarget.IsBound
					&& !document.Binding.CompletedChecklistItems.Contains( checklistId ) )
					document.Binding.CompletedChecklistItems.Add( checklistId );
			} );
	}

	private void SaveGripPose()
	{
		var document = _controller.Document;
		var skeleton = HostSkeletonBuilder.BuildCached( document );
		var pose = AnimationPoseEvaluator.Evaluate(
			document,
			skeleton,
			document.GetSelectedClip(),
			document.Workspace.TimelineTime,
			includeWorkingPose: true );
		_controller.Mutate( "Save default grip pose", d =>
		{
			var grip = new GripPose
			{
				Name = $"Grip {d.Binding.GripPoses.Count + 1}",
				Bones = skeleton.Bones
					.Where( x => !x.IsWeaponBone
						&& (x.Name.Contains( "finger_", StringComparison.OrdinalIgnoreCase )
							|| x.Name.Contains( "clavicle_", StringComparison.OrdinalIgnoreCase )
							|| x.Name.Contains( "hand_", StringComparison.OrdinalIgnoreCase )) )
					.Select( x => new BonePose
					{
						BoneName = x.Name,
						LocalTransform = pose.Local[x.Name]
					} )
					.ToList()
			};
			d.Binding.GripPoses.Add( grip );
			d.Binding.DefaultGripPoseId = grip.Id;
			d.Binding.CompletedChecklistItems.Add( "default_grip" );
		} );
	}

	private void ToggleGripConfiguration()
	{
		_controller.Mutate( "Grip configuration", d =>
			d.Binding.Configuration = d.Binding.Configuration == GripConfiguration.TwoHanded
				? GripConfiguration.OneHanded
				: GripConfiguration.TwoHanded );
	}

	private void ShowGripPoseMenu()
	{
		if ( _controller.Document.Binding.GripPoses.Count == 0 )
		{
			StatusChanged?.Invoke( "No reusable grip poses have been saved.", ValidationSeverity.Warning );
			return;
		}

		var menu = new Menu( this );
		foreach ( var grip in _controller.Document.Binding.GripPoses )
		{
			var captured = grip;
			menu.AddOption( captured.Name, null, () => ApplyGripPose( captured ) );
		}
		menu.OpenAtCursor();
	}

	private void ApplyGripPose( GripPose pose )
	{
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null )
			return;

		_controller.Mutate( $"Apply {pose.Name}", document =>
		{
			var time = document.Workspace.TimelineTime;
			foreach ( var bone in pose.Bones )
			{
				var track = clip.EnsureTrack( bone.BoneName );
				track.Kind = RigControlKind.Arm;
				WeaponAnimationMath.UpsertKey( track, time, bone.LocalTransform );
			}
			clip.Readiness = clip.Role == WeaponClipRole.Idle
				? ClipReadiness.Ready
				: ClipReadiness.Draft;
			document.Binding.DefaultGripPoseId = pose.Id;
		} );
	}

	private void AddConstraint()
	{
		var clip = _controller.Document.GetSelectedClip();
		var source = _controller.Document.Workspace.SelectedControl;
		var target = _controller.Document.Workspace.ConstraintTargetBone;
		if ( clip is null || string.IsNullOrWhiteSpace( source ) || string.IsNullOrWhiteSpace( target ) )
		{
			StatusChanged?.Invoke(
				"Select an arm control and a weapon bone before adding a constraint.",
				ValidationSeverity.Warning );
			return;
		}

		_controller.Mutate( "Add timed constraint", _ => clip.Constraints.Add( new TimedConstraint
		{
			SourceControl = source,
			TargetBone = target,
			StartTime = _controller.Document.Workspace.TimelineTime,
			EndTime = clip.Duration
		} ) );
	}

	private void ShowConstraintTargetMenu()
	{
		var menu = new Menu( this );
		var weaponBones = _controller.Document.Rig.RetainedBones()
			.OrderBy( x => x.Name );
		foreach ( var bone in weaponBones )
		{
			var captured = bone.Name;
			menu.AddOption( captured, null, () => _controller.Mutate(
				"Constraint target",
				d => d.Workspace.ConstraintTargetBone = captured ) );
		}
		menu.OpenAtCursor();
	}

	private void AddTag( string name, AnimationTagKind kind )
	{
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null || string.IsNullOrWhiteSpace( name ) )
			return;
		_controller.Mutate( $"Add tag {name}", document =>
		{
			var start = document.Workspace.TimelineTime;
			clip.Tags.Add( new AnimationTag
			{
				Name = name.Trim(),
				Kind = kind,
				StartTime = start,
				EndTime = kind == AnimationTagKind.Range
					? MathF.Min( start + 0.1f, clip.Duration )
					: start
			} );
		} );
	}

	private void SimulateParameter( string name, WeaponClipRole role )
	{
		var clip = _controller.Document.Clips.FirstOrDefault( x => x.Role == role );
		if ( clip is null )
			return;
		_controller.Document.Graph.PreviewBools[name] = true;
		_controller.SelectClip( clip.Id );
		StatusChanged?.Invoke(
			$"Simulating {name}=true with {(clip.Readiness == ClipReadiness.NotStarted ? "Idle fallback" : clip.Name)}.",
			clip.Readiness == ClipReadiness.NotStarted ? ValidationSeverity.Warning : ValidationSeverity.Info );
	}

	private string SelectionName()
	{
		var workspace = _controller.Document.Workspace;
		if ( !string.IsNullOrWhiteSpace( workspace.SelectedControl ) )
			return workspace.SelectedControl.TrimStart( '@' ).Replace( '_', ' ' );
		if ( !string.IsNullOrWhiteSpace( workspace.SelectedBone ) )
			return workspace.SelectedBone;
		return "No control selected";
	}

	private RigTarget? ResolveControl( string name ) => name switch
	{
		"@primary_hand" => _controller.Document.Binding.PrimaryHand,
		"@support_hand" => _controller.Document.Binding.SupportHand,
		"@primary_elbow" => _controller.Document.Binding.PrimaryElbowPole,
		"@support_elbow" => _controller.Document.Binding.SupportElbowPole,
		_ => null
	};

	private Widget NumericField(
		string name,
		float value,
		Action<float> changed,
		Widget? parent = null )
	{
		parent ??= _canvas;
		var row = RigAuditPanel.Row( parent );
		row.Layout.Add( WeaponAnimatorTheme.Label( name, row, true ), 1 );
		var edit = new LineEdit( row )
		{
			Text = value.ToString( "0.###", CultureInfo.InvariantCulture ),
			FixedHeight = 26,
			FixedWidth = 86
		};
		edit.SetStyles( WeaponAnimatorTheme.InputStyle );
		edit.EditingFinished += () =>
		{
			if ( float.TryParse( edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed ) )
				changed( parsed );
		};
		row.Layout.Add( edit );
		return row;
	}

	private Button ChoiceButton(
		string label,
		Func<string> current,
		IEnumerable<string> values,
		Action<string> changed,
		Widget? parent = null )
	{
		parent ??= _canvas;
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

	private static Button ToggleButton(
		string text,
		bool value,
		Action<bool> changed,
		Widget parent )
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

	private Label Header( string text )
	{
		return WeaponAnimatorTheme.SectionLabel( text, _canvas, topMargin: true );
	}

	private static TrackInterpolation DominantInterpolation( WeaponAnimationClip clip ) =>
		clip.Tracks.GroupBy( x => x.Interpolation )
			.OrderByDescending( x => x.Count() )
			.Select( x => x.Key )
			.FirstOrDefault();
}

public sealed class AnimationTimelinePanel : Widget
{
	private readonly WeaponAnimatorController _controller;
	private readonly TimelineEditorCanvas _timeline;
	private readonly TimelineControlToolbar _toolbar;
	private readonly Label _timeLabel;
	private readonly WeaponAnimatorButton _playButton;
	private readonly WeaponAnimatorButton _curvesButton;
	private readonly WeaponAnimatorButton _loopButton;

	public AnimationTimelinePanel( WeaponAnimatorController controller, Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		_toolbar = new TimelineControlToolbar( this );
		var left = _toolbar.LeftSection;
		left.Layout.Add( CompactAction( "Add key", "key", AddKey, left, true ) );
		left.Layout.Add( CompactAction( "Copy", "content_copy", _controller.CopySelectedKeys, left ) );
		left.Layout.Add( CompactAction( "Paste", "content_paste", _controller.PasteKeys, left ) );
		left.Layout.Add( CompactAction( "Delete", "delete", _controller.DeleteSelectedKeys, left ) );
		var reverse = CompactAction( "Reverse", "swap_horiz", _controller.ReverseKeys, left );
		reverse.ToolTip =
			"Reverse selected keys within their time range. With no selection, reverse the whole clip.";
		left.Layout.Add( reverse );
		_curvesButton = CompactAction(
			"Curves",
			"show_chart",
			() => _controller.SetCurveEditorVisible(
				!_controller.Document.Workspace.CurveEditorVisible ),
			left );
		_curvesButton.IsToggle = true;
		left.Layout.Add( _curvesButton );

		var player = _toolbar.CenterSection;
		player.Layout.Spacing = 3;
		player.Layout.Add( new Widget( player )
		{
			FixedWidth = 28,
			MinimumWidth = 28,
			FixedHeight = 26
		} );
		player.Layout.Add( PlayerButton( "first_page", "Jump to first frame", _controller.JumpToFirstFrame, player ) );
		player.Layout.Add( PlayerButton( "skip_previous", "Previous frame", () => _controller.StepTimelineFrame( -1 ), player ) );
		_playButton = PlayerButton( "play_arrow", "Play", _controller.TogglePlayback, player );
		player.Layout.Add( _playButton );
		player.Layout.Add( PlayerButton( "skip_next", "Next frame", () => _controller.StepTimelineFrame( 1 ), player ) );
		player.Layout.Add( PlayerButton( "last_page", "Jump to last frame", _controller.JumpToLastFrame, player ) );
		_loopButton = PlayerButton(
			"repeat",
			"Loop playback",
			_controller.ToggleSelectedClipLoop,
			player );
		_loopButton.IsToggle = true;
		_loopButton.Flat = true;
		player.Layout.Add( _loopButton );

		var right = _toolbar.RightSection;
		right.Layout.AddStretchCell();
		_timeLabel = WeaponAnimatorTheme.Label( "", right );
		right.Layout.Add( _timeLabel );
		_toolbar.FitSections();
		Layout.Add( _toolbar );

		_timeline = new TimelineEditorCanvas( controller, this );
		Layout.Add( _timeline, 1 );
		_controller.DocumentChanged += Refresh;
		_controller.TimelineChanged += Refresh;
		_controller.TimelineViewChanged += Refresh;
		_controller.PlaybackChanged += Refresh;
		_controller.ClipPlaybackSettingsChanged += Refresh;
		Refresh();
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Refresh;
		_controller.TimelineChanged -= Refresh;
		_controller.TimelineViewChanged -= Refresh;
		_controller.PlaybackChanged -= Refresh;
		_controller.ClipPlaybackSettingsChanged -= Refresh;
		base.OnDestroyed();
	}

	private void AddKey()
		=> _controller.KeySelectedTransform();

	private void Refresh()
	{
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null )
			_timeLabel.Text = "No clip";
		else
		{
			var frame = TimelineInteraction.TimeToFrame(
				_controller.Document.Workspace.TimelineTime,
				clip.SampleRate );
			var total = TimelineInteraction.LastFrame( clip );
			_timeLabel.Text =
				$"{_controller.Document.Workspace.TimelineTime:0.000}s · {frame:00} / {total:00}";
		}
		_playButton.Icon = _controller.IsPlaying ? "pause" : "play_arrow";
		_playButton.ToolTip = _controller.IsPlaying ? "Pause" : "Play";
		_loopButton.Enabled = clip is not null;
		_loopButton.IsChecked = clip?.Loop == true;
		_loopButton.Tint = clip?.Loop == true
			? WeaponAnimatorTheme.Cyan
			: WeaponAnimatorTheme.Muted;
		_loopButton.ToolTip = clip?.Loop == true
			? "Loop playback is enabled"
			: "Loop playback";
		var curves = _controller.Document.Workspace.CurveEditorVisible;
		_curvesButton.IsChecked = curves;
		_curvesButton.Text = curves ? "Keys" : "Curves";
		_curvesButton.Icon = curves ? "view_timeline" : "show_chart";
		_curvesButton.Tint = curves
			? WeaponAnimatorTheme.Cyan * 0.65f
			: WeaponAnimatorTheme.SurfaceRaised;
		_curvesButton.ToolTip = curves
			? "Return to the keyframe view"
			: "Open the curve editor";
		_curvesButton.FitToContent( true );
		_toolbar.FitSections();
		_timeline.Update();
	}

	private static WeaponAnimatorButton CompactAction(
		string text,
		string icon,
		Action clicked,
		Widget parent,
		bool primary = false )
	{
		var button = (WeaponAnimatorButton)WeaponAnimatorTheme.Button(
			text,
			icon,
			clicked,
			parent,
			primary );
		button.FixedHeight = 26;
		button.FitToContent( true );
		return button;
	}

	private static WeaponAnimatorButton PlayerButton(
		string icon,
		string tooltip,
		Action clicked,
		Widget parent )
	{
		var button = new WeaponAnimatorButton( "", icon, parent )
		{
			Clicked = clicked,
			FixedWidth = 28,
			FixedHeight = 26,
			Tint = WeaponAnimatorTheme.SurfaceRaised,
			ToolTip = tooltip
		};
		return button;
	}
}

internal sealed class TimelineControlToolbar : Widget
{
	public Widget LeftSection { get; }
	public Widget CenterSection { get; }
	public Widget RightSection { get; }

	public TimelineControlToolbar( Widget? parent = null ) : base( parent )
	{
		FixedHeight = 34;
		SetStyles( "background-color: rgb(24,27,30); border: none;" );
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 7, 4, 7, 4 );
		Layout.Spacing = 0;

		LeftSection = Section( this );
		CenterSection = Section( this );
		RightSection = Section( this );
		Layout.Add( LeftSection );
		Layout.AddStretchCell();
		Layout.Add( RightSection );
		CenterSection.Raise();
	}

	public void FitSections()
	{
		LeftSection.FixedWidth = SectionWidth( LeftSection );
		CenterSection.FixedWidth = SectionWidth( CenterSection );
		PositionCenter();
	}

	protected override void OnResize()
	{
		base.OnResize();
		PositionCenter();
	}

	private void PositionCenter()
	{
		CenterSection.Position = new Vector2(
			CenteredLeft( Width, CenterSection.Width ),
			MathF.Round( (Height - CenterSection.Height) * 0.5f ) );
		CenterSection.Raise();
	}

	internal static float CenteredLeft( float toolbarWidth, float sectionWidth ) =>
		MathF.Round( (toolbarWidth - sectionWidth) * 0.5f );

	private static float SectionWidth( Widget section )
	{
		var children = section.Children.ToArray();
		if ( children.Length == 0 )
			return 0;
		return children.Sum( x => x is WeaponAnimatorButton button
			? string.IsNullOrWhiteSpace( button.Text )
				? 28
				: MathF.Ceiling( button.PreferredWidth )
			: MathF.Max( x.MinimumWidth, 0 ) )
			+ MathF.Max( children.Length - 1, 0 ) * section.Layout.Spacing;
	}

	private static Widget Section( Widget parent )
	{
		var section = new Widget( parent )
		{
			Layout = Layout.Row(),
			FixedHeight = 26
		};
		section.SetStyles( "background-color: transparent; border: none;" );
		section.Layout.Margin = 0;
		section.Layout.Spacing = 4;
		return section;
	}
}

internal static class ClipExtensions
{
	public static void KeysClampToDuration( this WeaponAnimationClip clip )
	{
		foreach ( var key in clip.Tracks.SelectMany( x => x.Keys ) )
			key.Time = Math.Clamp( key.Time, 0, clip.Duration );
		foreach ( var key in clip.VisibilityTracks.SelectMany( x => x.Keys ) )
			key.Time = Math.Clamp( key.Time, 0, clip.Duration );
		foreach ( var tag in clip.Tags )
		{
			tag.StartTime = Math.Clamp( tag.StartTime, 0, clip.Duration );
			tag.EndTime = Math.Clamp( tag.EndTime, tag.StartTime, clip.Duration );
		}
	}
}
