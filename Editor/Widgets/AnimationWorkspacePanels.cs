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
	private readonly Widget _clipCanvas;
	private readonly Widget? _rigCanvas;
	private readonly Widget? _propertiesCanvas;
	private readonly Label _actionHint;
	private readonly bool _clipsOnly;

	public event Action<string, ValidationSeverity>? StatusChanged;

	public ClipRackPanel(
		WeaponAnimatorController controller,
		Widget? parent = null,
		bool clipsOnly = false,
		bool showClipHeader = true ) : base( parent )
	{
		_controller = controller;
		_clipsOnly = clipsOnly;
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 8 );
		Layout.Spacing = 6;

		if ( !_clipsOnly )
			Layout.Add( BuildChecklist() );

		if ( showClipHeader )
			Layout.Add( Header( "CLIP RACK", this ) );
		var clipScroll = new ScrollArea( this )
		{
			MinimumSize = new Vector2( 200, clipsOnly ? 70 : 160 )
		};
		_clipCanvas = new Widget( clipScroll );
		_clipCanvas.Layout = Layout.Column();
		_clipCanvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin();
		_clipCanvas.Layout.Spacing = 2;
		clipScroll.Canvas = _clipCanvas;
		Layout.Add( clipScroll, 2 );

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

		if ( !_clipsOnly )
		{
			Layout.Add( Header( "RIG TREE", this ) );
			var rigScroll = new ScrollArea( this ) { MinimumSize = new Vector2( 200, 120 ) };
			_rigCanvas = new Widget( rigScroll );
			_rigCanvas.Layout = Layout.Column();
			_rigCanvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin();
			_rigCanvas.Layout.Spacing = 1;
			rigScroll.Canvas = _rigCanvas;
			Layout.Add( rigScroll, 1 );
		}
		else
		{
			var propertiesScroll = new ScrollArea( this ) { MinimumHeight = 80 };
			_propertiesCanvas = new Widget( propertiesScroll );
			_propertiesCanvas.Layout = Layout.Column();
			_propertiesCanvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin();
			_propertiesCanvas.Layout.Spacing = 4;
			propertiesScroll.Canvas = _propertiesCanvas;
			Layout.Add( propertiesScroll, 1 );
		}

		var addCustom = WeaponAnimatorTheme.Button(
			"Add custom clip",
			"playlist_add",
			AddCustomClip,
			this );
		Layout.Add( addCustom );

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

	private Widget BuildChecklist()
	{
		var widget = new Widget( this );
		widget.SetStyles(
			"background-color: rgba(21,127,150,0.12);" +
			"border: 1px solid rgba(55,198,226,0.24);" +
			"border-radius: 3px;" );
		widget.Layout = Layout.Column();
		widget.Layout.Margin = new Sandbox.UI.Margin( 8 );
		widget.Layout.Spacing = 3;
		widget.Visible = !_controller.Document.Binding.ChecklistDismissed;

		var title = WeaponAnimatorTheme.SectionLabel(
			"FIRST GRIP CHECKLIST",
			widget,
			WeaponAnimatorTheme.Cyan );
		widget.Layout.Add( title );

		var items = new[]
		{
			("1  Bind primary hand", "@primary_hand"),
			("2  Bind support hand", "@support_hand"),
			("3  Elbow poles", "@primary_elbow"),
			("4  Pose fingers around grips", "finger_index_0_R"),
			("5  Save default grip pose", "")
		};
		foreach ( var item in items )
		{
			var button = new WeaponAnimatorButton( item.Item1, widget )
			{
				Clicked = () =>
				{
					if ( !string.IsNullOrWhiteSpace( item.Item2 ) )
					{
						if ( item.Item2.StartsWith( "@" ) )
							_controller.SelectControl( item.Item2 );
						else
							_controller.SelectBone( item.Item2 );
					}
				},
				Tint = WeaponAnimatorTheme.Surface
			};
			widget.Layout.Add( button );
		}
		widget.Layout.Add( WeaponAnimatorTheme.Button(
			"Dismiss checklist",
			"close",
			() =>
			{
				_controller.Mutate( "Dismiss binding checklist", d => d.Binding.ChecklistDismissed = true );
				widget.Visible = false;
			},
			widget ) );
		return widget;
	}

	private void Rebuild()
	{
		_clipCanvas?.Layout.Clear( true );
		_rigCanvas?.Layout.Clear( true );
		_propertiesCanvas?.Layout.Clear( true );
		if ( _clipCanvas is null )
			return;

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

		var custom = _controller.Document.Clips.Where( x => x.Role == WeaponClipRole.Custom ).ToArray();
		if ( custom.Length > 0 )
		{
			_clipCanvas.Layout.Add( Header( "CUSTOM", _clipCanvas ) );
			foreach ( var clip in custom )
				AddClipButton( clip );
		}
		_clipCanvas.Layout.AddStretchCell();

		if ( _rigCanvas is not null )
		{
			var controls = new[]
			{
				("@primary_hand",
					$"Primary hand · {(_controller.Document.Binding.PrimaryHand.IsBound ? "bound" : "unbound")}",
					WeaponAnimatorTheme.Cyan),
				("@support_hand",
					$"Support hand · {(_controller.Document.Binding.SupportHand.IsBound ? "bound" : "unbound")}",
					WeaponAnimatorTheme.Cyan),
				("@primary_elbow", "Primary elbow", WeaponAnimatorTheme.Cyan),
				("@support_elbow", "Support elbow", WeaponAnimatorTheme.Cyan)
			};
			foreach ( var control in controls )
			{
				var button = new WeaponAnimatorButton( control.Item2, _rigCanvas )
				{
					Clicked = () => _controller.SelectControl( control.Item1 ),
					Tint = _controller.Document.Workspace.SelectedControl == control.Item1
						? control.Item3 * 0.5f
						: WeaponAnimatorTheme.Surface
				};
				_rigCanvas.Layout.Add( button );
			}

			foreach ( var bone in HostSkeletonBuilder.Build( _controller.Document ).Bones )
			{
				var button = new WeaponAnimatorButton( bone.Name, _rigCanvas )
				{
					Clicked = () => _controller.SelectBone( bone.Name ),
					Tint = _controller.Document.Workspace.SelectedBone == bone.Name
						? (bone.IsWeaponBone ? WeaponAnimatorTheme.Amber : WeaponAnimatorTheme.Cyan) * 0.45f
						: WeaponAnimatorTheme.Surface
				};
				_rigCanvas.Layout.Add( button );
			}
			_rigCanvas.Layout.AddStretchCell();
		}

		var selected = _controller.Document.GetSelectedClip();
		_actionHint.Text = selected is null
			? "Select a clip."
			: selected.Readiness == ClipReadiness.NotStarted
				? "Not started · choose Start, Duplicate, or Import."
				: $"{selected.Readiness} · {selected.Duration:0.###} s at {selected.SampleRate:0.#} fps";
		if ( _propertiesCanvas is not null )
			BuildClipProperties( selected );
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
		var subframes = new WeaponAnimatorButton( "Allow subframe keys", _propertiesCanvas )
		{
			IsToggle = true,
			IsChecked = clip.AllowSubframeKeys,
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		subframes.Toggled = () => _controller.Mutate(
			"Subframe keys",
			_ => clip.AllowSubframeKeys = subframes.IsChecked );
		_propertiesCanvas.Layout.Add( subframes );

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
		var marker = clip.Readiness switch
		{
			ClipReadiness.NotStarted => "○",
			ClipReadiness.Draft => "◐",
			ClipReadiness.Ready => "●",
			_ => "!"
		};
		var button = new WeaponAnimatorButton( $"{marker}  {clip.Name}", _clipCanvas )
		{
			Clicked = () => _controller.SelectClip( clip.Id ),
			Tint = clip.Id == _controller.Document.Workspace.SelectedClipId
				? WeaponAnimatorTheme.Cyan * 0.42f
				: clip.Readiness switch
				{
					ClipReadiness.Ready => WeaponAnimatorTheme.Green * 0.24f,
					ClipReadiness.Warning => WeaponAnimatorTheme.Coral * 0.28f,
					_ => WeaponAnimatorTheme.Surface
				},
			ToolTip = clip.Readiness.ToString()
		};
		_clipCanvas.Layout.Add( button );
	}

	private void StartSelectedFromDefault()
	{
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null )
			return;

		_controller.Mutate( $"Start {clip.Name}", document =>
		{
			document.Workspace.ClearWorkingPoses( clip.Id );
			var skeleton = HostSkeletonBuilder.Build( document );
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
			var copy = Json.Deserialize<WeaponAnimationClip>( Json.Serialize( source ) )!;
			destination.Duration = copy.Duration;
			destination.SampleRate = copy.SampleRate;
			destination.AllowSubframeKeys = copy.AllowSubframeKeys;
			destination.IsBindPoseSeed = false;
			destination.Tracks = copy.Tracks;
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

				var weaponBones = HostSkeletonBuilder.Build( _controller.Document )
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
		var skeleton = HostSkeletonBuilder.Build( document );
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
	private readonly TimelineCanvas _timeline;
	private readonly Label _timeLabel;

	public AnimationTimelinePanel( WeaponAnimatorController controller, Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		var toolbar = new Widget( this ) { FixedHeight = 34 };
		toolbar.SetStyles(
			"background-color: rgb(24,27,30); border-bottom: 1px solid rgba(255,255,255,0.07);" );
		toolbar.Layout = Layout.Row();
		toolbar.Layout.Margin = new Sandbox.UI.Margin( 7, 4, 7, 4 );
		toolbar.Layout.Spacing = 4;
		toolbar.Layout.Add( WeaponAnimatorTheme.Button( "Add key", "key", AddKey, toolbar, true ) );
		toolbar.Layout.Add( WeaponAnimatorTheme.Button( "Copy", "content_copy", _controller.CopySelectedKeys, toolbar ) );
		toolbar.Layout.Add( WeaponAnimatorTheme.Button( "Paste", "content_paste", _controller.PasteKeys, toolbar ) );
		toolbar.Layout.Add( WeaponAnimatorTheme.Button( "Mirror", "flip", _controller.MirrorSelectedKeys, toolbar ) );
		toolbar.Layout.Add( WeaponAnimatorTheme.Button(
			"Curves",
			"show_chart",
			() => _controller.Mutate(
				"Curve editor visibility",
				d => d.Workspace.CurveEditorVisible = !d.Workspace.CurveEditorVisible ),
			toolbar ) );
		toolbar.Layout.AddStretchCell();
		_timeLabel = WeaponAnimatorTheme.Label( "", toolbar );
		toolbar.Layout.Add( _timeLabel );
		Layout.Add( toolbar );

		_timeline = new TimelineCanvas( controller, this );
		Layout.Add( _timeline, 1 );
		_controller.DocumentChanged += Refresh;
		_controller.TimelineChanged += Refresh;
		Refresh();
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Refresh;
		_controller.TimelineChanged -= Refresh;
		base.OnDestroyed();
	}

	private void AddKey()
	{
		var document = _controller.Document;
		var selectedControl = document.Workspace.SelectedControl;
		if ( !string.IsNullOrWhiteSpace( selectedControl ) )
		{
			var target = selectedControl switch
			{
				"@primary_hand" => document.Binding.PrimaryHand,
				"@support_hand" => document.Binding.SupportHand,
				"@primary_elbow" => document.Binding.PrimaryElbowPole,
				"@support_elbow" => document.Binding.SupportElbowPole,
				_ => null
			};
			if ( target is not null )
			{
				var controlClip = document.GetSelectedClip();
				var controlFallback = controlClip is null
					? target.Transform
					: controlClip.Tracks.FirstOrDefault( x =>
						x.Target.Equals( selectedControl, StringComparison.OrdinalIgnoreCase ) ) is { } controlTrack
						? WeaponAnimationMath.SampleTrack(
							controlTrack,
							document.Workspace.TimelineTime,
							target.Transform )
						: target.Transform;
				_controller.CommitWorkingPose( selectedControl, RigControlKind.Arm, controlFallback );
			}
			return;
		}

		var selectedBone = document.Workspace.SelectedBone;
		if ( string.IsNullOrWhiteSpace( selectedBone ) )
			return;
		var skeleton = HostSkeletonBuilder.Build( document );
		if ( !skeleton.ByName.TryGetValue( selectedBone, out var bone ) )
			return;
		var clip = document.GetSelectedClip();
		var fallback = skeleton.GetBindLocal( bone );
		var track = clip?.Tracks.FirstOrDefault( x => x.Target == selectedBone );
		var value = track is null
			? fallback
			: WeaponAnimationMath.SampleTrack( track, document.Workspace.TimelineTime, fallback );
		_controller.CommitWorkingPose(
			selectedBone,
			bone.IsWeaponBone ? RigControlKind.Weapon : RigControlKind.Arm,
			value );
	}

	private void Refresh()
	{
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null )
			_timeLabel.Text = "No clip";
		else
		{
			var frame = (int)MathF.Round(
				_controller.Document.Workspace.TimelineTime * clip.SampleRate );
			var total = (int)MathF.Round( clip.Duration * clip.SampleRate );
			_timeLabel.Text =
				$"{_controller.Document.Workspace.TimelineTime:0.000}s · {frame:00} / {total:00}";
		}
		_timeline.Update();
	}
}

internal sealed class TimelineCanvas : Widget
{
	private const float TrackHeaderWidth = 170;
	private const float RulerHeight = 24;
	private const float TrackHeight = 22;
	private readonly WeaponAnimatorController _controller;
	private Vector2 _pressPosition;
	private Dictionary<Guid, float>? _dragStartTimes;

	public TimelineCanvas( WeaponAnimatorController controller, Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		MinimumSize = new Vector2( 320, 120 );
		MouseTracking = true;
		SetStyles( "background-color: rgb(12,14,16); border: none;" );
		_controller.DocumentChanged += Update;
		_controller.SelectionChanged += Update;
		_controller.TimelineChanged += Update;
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Update;
		_controller.SelectionChanged -= Update;
		_controller.TimelineChanged -= Update;
		base.OnDestroyed();
	}

	protected override void OnPaint()
	{
		var clip = _controller.Document.GetSelectedClip();
		Paint.SetBrushAndPen( WeaponAnimatorTheme.Background );
		Paint.DrawRect( LocalRect );
		if ( clip is null )
			return;

		var bodyWidth = MathF.Max( Width - TrackHeaderWidth, 1 );
		Paint.SetPen( WeaponAnimatorTheme.Border );
		Paint.DrawLine( new Vector2( TrackHeaderWidth, 0 ), new Vector2( TrackHeaderWidth, Height ) );

		var totalFrames = Math.Max( 1, (int)MathF.Round( clip.Duration * clip.SampleRate ) );
		var frameStep = totalFrames > 120 ? 10 : totalFrames > 60 ? 5 : 1;
		for ( var frame = 0; frame <= totalFrames; frame += frameStep )
		{
			var time = frame / clip.SampleRate;
			var x = TimeToX( time, clip.Duration );
			Paint.SetPen( Color.White.WithAlpha( frame % (frameStep * 5) == 0 ? 0.12f : 0.05f ) );
			Paint.DrawLine( new Vector2( x, RulerHeight ), new Vector2( x, Height ) );
			if ( frame % (frameStep * 5) == 0 )
			{
				Paint.SetPen( WeaponAnimatorTheme.Muted );
				Paint.DrawText(
					new Rect( x + 3, 0, 42, RulerHeight ),
					frame.ToString(),
					TextFlag.LeftCenter );
			}
		}

		if ( _controller.Document.Workspace.CurveEditorVisible )
			DrawCurves( clip );
		else
			DrawDopeSheet( clip );

		var playheadX = TimeToX(
			_controller.Document.Workspace.TimelineTime,
			clip.Duration );
		Paint.SetPen( WeaponAnimatorTheme.Coral, 1.5f );
		Paint.DrawLine( new Vector2( playheadX, 0 ), new Vector2( playheadX, Height ) );
		Paint.SetBrushAndPen( WeaponAnimatorTheme.Coral );
		Paint.DrawRect( new Rect( playheadX - 3, 0, 6, 8 ), 1 );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		if ( !e.LeftMouseButton )
			return;
		_pressPosition = e.LocalPosition;
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null || e.LocalPosition.x < TrackHeaderWidth )
			return;

		var hit = HitKey( clip, e.LocalPosition );
		if ( hit is not null )
		{
			_controller.SelectKeys( [hit.Id], e.HasCtrl || e.HasShift );
			_dragStartTimes = clip.Tracks
				.SelectMany( x => x.Keys )
				.Where( x => _controller.SelectedKeys.Contains( x.Id ) )
				.ToDictionary( x => x.Id, x => x.Time );
		}
		else
		{
			_controller.SelectKeys( [], false );
			_controller.SetTimelineTime( XToTime( e.LocalPosition.x, clip.Duration ) );
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		if ( (e.ButtonState & MouseButtons.Left) == 0 || _dragStartTimes is null )
			return;
		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( _dragStartTimes is null || !e.LeftMouseButton )
			return;
		var clip = _controller.Document.GetSelectedClip();
		if ( clip is null )
		{
			_dragStartTimes = null;
			return;
		}

		var delta = XToTime( e.LocalPosition.x, clip.Duration )
			- XToTime( _pressPosition.x, clip.Duration );
		var starts = _dragStartTimes;
		_dragStartTimes = null;
		if ( MathF.Abs( delta ) < 0.00001f )
			return;

		_controller.Mutate( "Move keys", _ =>
		{
			foreach ( var key in clip.Tracks.SelectMany( x => x.Keys ) )
			{
				if ( !starts.TryGetValue( key.Id, out var start ) )
					continue;
				key.Time = Math.Clamp(
					WeaponAnimationMath.SnapTime(
						start + delta,
						clip.SampleRate,
						clip.AllowSubframeKeys ),
					0,
					clip.Duration );
			}
			foreach ( var track in clip.Tracks )
				track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
		} );
	}

	private void DrawDopeSheet( WeaponAnimationClip clip )
	{
		var tracks = clip.Tracks.Take( Math.Max( 1, (int)((Height - RulerHeight) / TrackHeight) - 1 ) ).ToArray();
		for ( var i = 0; i < tracks.Length; i++ )
		{
			var track = tracks[i];
			var y = RulerHeight + i * TrackHeight;
			Paint.SetBrushAndPen( i % 2 == 0
				? Color.White.WithAlpha( 0.018f )
				: Color.Transparent );
			Paint.DrawRect( new Rect( 0, y, Width, TrackHeight ) );
			Paint.SetPen( track.Kind == RigControlKind.Weapon
				? WeaponAnimatorTheme.Amber
				: WeaponAnimatorTheme.Cyan );
			Paint.DrawText( new Rect( 8, y, TrackHeaderWidth - 12, TrackHeight ), track.Target, TextFlag.LeftCenter );

			foreach ( var key in track.Keys )
			{
				var x = TimeToX( key.Time, clip.Duration );
				var selected = _controller.SelectedKeys.Contains( key.Id );
				Paint.SetBrushAndPen( selected ? Color.White : TrackColor( track.Kind ) );
				var diamond = new[]
				{
					new Vector2( x, y + 5 ),
					new Vector2( x + 5, y + TrackHeight * 0.5f ),
					new Vector2( x, y + TrackHeight - 5 ),
					new Vector2( x - 5, y + TrackHeight * 0.5f )
				};
				Paint.DrawPolygon( diamond );
			}
		}

		var tagY = RulerHeight + tracks.Length * TrackHeight;
		Paint.SetPen( WeaponAnimatorTheme.Muted );
		Paint.DrawText( new Rect( 8, tagY, TrackHeaderWidth - 12, TrackHeight ), "TAGS", TextFlag.LeftCenter );
		foreach ( var tag in clip.Tags )
		{
			var start = TimeToX( tag.StartTime, clip.Duration );
			var end = TimeToX( tag.EndTime, clip.Duration );
			Paint.SetBrushAndPen( WeaponAnimatorTheme.Green.WithAlpha( 0.65f ) );
			if ( tag.Kind == AnimationTagKind.Point )
				Paint.DrawRect( new Rect( start - 2, tagY + 4, 4, TrackHeight - 8 ), 1 );
			else
				Paint.DrawRect( new Rect( start, tagY + 5, MathF.Max( end - start, 4 ), TrackHeight - 10 ), 2 );
		}
	}

	private void DrawCurves( WeaponAnimationClip clip )
	{
		var tracks = clip.Tracks.Where( x => x.Keys.Count > 0 ).Take( 3 ).ToArray();
		if ( tracks.Length == 0 )
			return;
		var graph = new Rect( TrackHeaderWidth, RulerHeight, Width - TrackHeaderWidth, Height - RulerHeight );
		var values = tracks.SelectMany( x => x.Keys ).SelectMany( x =>
			new[] { x.Position.x, x.Position.y, x.Position.z } ).ToArray();
		var minimum = values.DefaultIfEmpty( -1 ).Min();
		var maximum = values.DefaultIfEmpty( 1 ).Max();
		if ( MathF.Abs( maximum - minimum ) < 0.001f )
		{
			minimum -= 1;
			maximum += 1;
		}

		for ( var index = 0; index < tracks.Length; index++ )
		{
			var track = tracks[index];
			Paint.SetPen( TrackColor( track.Kind ), 1.5f );
			Paint.DrawText(
				new Rect( 8, RulerHeight + index * 20, TrackHeaderWidth - 12, 20 ),
				track.Target,
				TextFlag.LeftCenter );
			Vector2? previous = null;
			foreach ( var key in track.Keys.OrderBy( x => x.Time ) )
			{
				var point = new Vector2(
					TimeToX( key.Time, clip.Duration ),
					graph.Bottom - (key.Position.x - minimum) / (maximum - minimum) * graph.Height );
				if ( previous is not null )
					Paint.DrawLine( previous.Value, point );
				Paint.DrawCircle( point, 3 );
				previous = point;
			}
		}
	}

	private TransformKey? HitKey( WeaponAnimationClip clip, Vector2 position )
	{
		var trackIndex = (int)((position.y - RulerHeight) / TrackHeight);
		if ( trackIndex < 0 || trackIndex >= clip.Tracks.Count )
			return null;
		var track = clip.Tracks[trackIndex];
		return track.Keys.FirstOrDefault( x =>
			MathF.Abs( TimeToX( x.Time, clip.Duration ) - position.x ) <= 8 );
	}

	private float TimeToX( float time, float duration ) =>
		TrackHeaderWidth + time / MathF.Max( duration, 0.0001f ) * MathF.Max( Width - TrackHeaderWidth, 1 );

	private float XToTime( float x, float duration ) =>
		Math.Clamp(
			(x - TrackHeaderWidth) / MathF.Max( Width - TrackHeaderWidth, 1 ) * duration,
			0,
			duration );

	private static Color TrackColor( RigControlKind kind ) => kind switch
	{
		RigControlKind.Weapon => WeaponAnimatorTheme.Amber,
		RigControlKind.Camera => WeaponAnimatorTheme.Green,
		_ => WeaponAnimatorTheme.Cyan
	};
}

internal static class ClipExtensions
{
	public static void KeysClampToDuration( this WeaponAnimationClip clip )
	{
		foreach ( var key in clip.Tracks.SelectMany( x => x.Keys ) )
			key.Time = Math.Clamp( key.Time, 0, clip.Duration );
		foreach ( var tag in clip.Tags )
		{
			tag.StartTime = Math.Clamp( tag.StartTime, 0, clip.Duration );
			tag.EndTime = Math.Clamp( tag.EndTime, tag.StartTime, clip.Duration );
		}
	}
}
