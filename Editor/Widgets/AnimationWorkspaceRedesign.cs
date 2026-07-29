#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class RigBrowserPanel : Widget
{
	private readonly WeaponAnimatorController _controller;
	private readonly ScrollArea _scroll;
	private readonly Widget _canvas;
	private readonly LineEdit _search;
	private readonly Dictionary<string, WeaponAnimatorButton> _itemButtons =
		new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, bool> _weaponItems =
		new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, bool> _expanded = new( StringComparer.OrdinalIgnoreCase )
	{
		["Controls"] = true,
		["Weapon"] = true,
		["Right arm"] = true,
		["Left arm"] = true,
		["Fingers"] = false,
		["Advanced"] = false
	};
	private string _filter = "";
	private bool _rebuilding;
	private bool _rebuildPending;
	private string _structureSignature = "";

	public RigBrowserPanel( WeaponAnimatorController controller, Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 8 );
		Layout.Spacing = 6;

		_search = new LineEdit( this )
		{
			PlaceholderText = "Search controls and bones…",
			FixedHeight = 28
		};
		_search.SetStyles( WeaponAnimatorTheme.InputStyle );
		_search.TextChanged += text =>
		{
			_filter = text.Trim();
			Rebuild();
		};
		Layout.Add( _search );

		_scroll = new ScrollArea( this );
		_canvas = new Widget( _scroll );
		_canvas.Layout = Layout.Column();
		_canvas.Layout.Margin = WeaponAnimatorTheme.ScrollCanvasMargin();
		_canvas.Layout.Spacing = 2;
		_scroll.Canvas = _canvas;
		Layout.Add( _scroll, 1 );

		_controller.DocumentChanged += RefreshDocument;
		_controller.SelectionChanged += RefreshSelection;
		Rebuild();
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= RefreshDocument;
		_controller.SelectionChanged -= RefreshSelection;
		base.OnDestroyed();
	}

	private void RefreshDocument()
	{
		var skeleton = HostSkeletonBuilder.BuildCached( _controller.Document );
		if ( _structureSignature != StructureSignature( skeleton ) )
		{
			Rebuild();
			return;
		}

		UpdateControlLabels();
		RefreshSelection();
	}

	private void Rebuild()
	{
		if ( _rebuilding )
		{
			_rebuildPending = true;
			return;
		}

		_rebuilding = true;
		try
		{
			do
			{
				_rebuildPending = false;
				_canvas.Layout.Clear( true );
				_itemButtons.Clear();
				_weaponItems.Clear();
				var skeleton = HostSkeletonBuilder.BuildCached( _controller.Document );
				_structureSignature = StructureSignature( skeleton );
				var groups = GroupBones( skeleton );
				AddControlGroup();
				foreach ( var name in new[] { "Weapon", "Right arm", "Left arm", "Fingers", "Advanced" } )
					AddBoneGroup( name, groups.GetValueOrDefault( name ) ?? [], skeleton );
				_canvas.Layout.AddStretchCell();
			}
			while ( _rebuildPending );
		}
		finally
		{
			_rebuilding = false;
		}

		RefreshSelection( reveal: false );
		UpdateControlLabels();
	}

	private void AddControlGroup()
	{
		var controls = new[]
		{
			("@primary_hand", $"Primary hand · {BoundText( _controller.Document.Binding.PrimaryHand )}"),
			("@support_hand", $"Support hand · {BoundText( _controller.Document.Binding.SupportHand )}"),
			("@primary_elbow", "Primary elbow"),
			("@support_elbow", "Support elbow")
		};
		var visible = controls.Where( x => Matches( x.Item2 ) ).ToArray();
		var body = AddGroup( "Controls", visible.Length );

		foreach ( var control in visible )
		{
			var button = new WeaponAnimatorButton( control.Item2, body )
			{
				Clicked = () => _controller.SelectControl( control.Item1 ),
				Tint = _controller.Document.Workspace.SelectedControl == control.Item1
					? WeaponAnimatorTheme.Cyan * 0.48f
					: WeaponAnimatorTheme.Surface
			};
			body.Layout.Add( button );
			_itemButtons[control.Item1] = button;
			_weaponItems[control.Item1] = false;
		}
	}

	private void AddBoneGroup(
		string name,
		IReadOnlyList<HostBone> bones,
		HostSkeleton skeleton )
	{
		var visible = bones.Where( x => Matches( x.Name ) ).ToArray();
		var body = AddGroup( name, visible.Length );

		foreach ( var bone in visible.OrderBy( x => x.Index ) )
		{
			var depth = HierarchyDepth( bone, skeleton );
			var row = RigAuditPanel.Row( body );
			row.FixedHeight = 28;
			var indentation = new Widget( row )
			{
				FixedWidth = Math.Min( depth, 8 ) * 12
			};
			row.Layout.Add( indentation );
			var button = new WeaponAnimatorButton(
				bone.Name,
				row )
			{
				Clicked = () => _controller.SelectBone( bone.Name ),
				Tint = _controller.Document.Workspace.SelectedBone == bone.Name
					? (bone.IsWeaponBone ? WeaponAnimatorTheme.Amber : WeaponAnimatorTheme.Cyan) * 0.48f
					: WeaponAnimatorTheme.Surface
			};
			row.Layout.Add( button, 1 );
			body.Layout.Add( row );
			_itemButtons[bone.Name] = button;
			_weaponItems[bone.Name] = bone.IsWeaponBone;
		}
	}

	private Widget AddGroup( string name, int count )
	{
		var selectedInGroup = SelectedGroup() == name;
		if ( selectedInGroup )
			_expanded[name] = true;
		var body = new Widget( _canvas )
		{
			Layout = Layout.Column()
		};
		body.Layout.Margin = 0;
		body.Layout.Spacing = 2;
		body.Visible = GroupBodyVisible( name );
		var header = new WeaponAnimatorButton(
			GroupHeaderText( name, count ),
			_canvas )
		{
			Tint = WeaponAnimatorTheme.SurfaceRaised
		};
		header.Clicked = () =>
		{
			_expanded[name] = !_expanded[name];
			body.Visible = GroupBodyVisible( name );
			header.Text = GroupHeaderText( name, count );
			body.UpdateGeometry();
		};
		header.FixedHeight = 25;
		_canvas.Layout.Add( header );
		_canvas.Layout.Add( body );
		return body;
	}

	private bool GroupBodyVisible( string name ) =>
		_expanded[name] || !string.IsNullOrWhiteSpace( _filter );

	private string GroupHeaderText( string name, int count ) =>
		$"{(GroupBodyVisible( name ) ? "▾" : "▸")}  {name.ToUpperInvariant()}  {count}";

	private string SelectedGroup()
	{
		if ( !string.IsNullOrWhiteSpace( _controller.Document.Workspace.SelectedControl ) )
			return "Controls";
		var selected = _controller.Document.Workspace.SelectedBone;
		if ( string.IsNullOrWhiteSpace( selected ) )
			return "";
		return GroupName( HostSkeletonBuilder.BuildCached( _controller.Document ).ByName.GetValueOrDefault( selected ) );
	}

	private Dictionary<string, List<HostBone>> GroupBones( HostSkeleton skeleton )
	{
		var groups = new Dictionary<string, List<HostBone>>( StringComparer.OrdinalIgnoreCase );
		foreach ( var bone in skeleton.Bones )
		{
			var group = GroupName( bone );
			if ( !groups.TryGetValue( group, out var list ) )
				groups[group] = list = [];
			list.Add( bone );
		}
		return groups;
	}

	internal static string GroupName( HostBone? bone )
	{
		if ( bone is null )
			return "Advanced";
		if ( bone.IsWeaponBone )
			return "Weapon";
		if ( bone.Name.Contains( "finger", StringComparison.OrdinalIgnoreCase )
			|| bone.Name.Contains( "thumb", StringComparison.OrdinalIgnoreCase ) )
			return "Fingers";
		if ( bone.Name.EndsWith( "_R", StringComparison.OrdinalIgnoreCase ) )
			return "Right arm";
		if ( bone.Name.EndsWith( "_L", StringComparison.OrdinalIgnoreCase ) )
			return "Left arm";
		return "Advanced";
	}

	private static int HierarchyDepth( HostBone bone, HostSkeleton skeleton )
	{
		var depth = 0;
		var parent = bone.ParentName;
		while ( !string.IsNullOrWhiteSpace( parent )
			&& skeleton.ByName.TryGetValue( parent, out var parentBone )
			&& depth < 16 )
		{
			depth++;
			parent = parentBone.ParentName;
		}
		return depth;
	}

	private bool Matches( string text ) =>
		string.IsNullOrWhiteSpace( _filter )
		|| text.Contains( _filter, StringComparison.OrdinalIgnoreCase );

	private void RefreshSelection()
	{
		RefreshSelection( reveal: true );
	}

	private void RefreshSelection( bool reveal )
	{
		if ( _rebuilding )
			return;

		var selected = SelectedItem();
		var group = SelectedGroup();
		if ( !string.IsNullOrWhiteSpace( group )
			&& _expanded.TryGetValue( group, out var expanded )
			&& !expanded )
		{
			_expanded[group] = true;
			Rebuild();
			return;
		}

		foreach ( var item in _itemButtons )
		{
			var isSelected = item.Key.Equals(
				selected,
				StringComparison.OrdinalIgnoreCase );
			var accent = _weaponItems.GetValueOrDefault( item.Key )
				? WeaponAnimatorTheme.Amber
				: WeaponAnimatorTheme.Cyan;
			item.Value.Tint = isSelected
				? accent * 0.48f
				: WeaponAnimatorTheme.Surface;
		}

		if ( reveal
			&& !string.IsNullOrWhiteSpace( selected )
			&& _itemButtons.TryGetValue( selected, out var button ) )
			RevealIfNeeded( button );
	}

	private void RevealIfNeeded( Widget button )
	{
		if ( button.Height <= 0 || _scroll.Height <= 0 )
			return;

		var viewportTop = _scroll.ScreenPosition.y;
		var viewportBottom = viewportTop + _scroll.Height;
		var itemTop = button.ScreenPosition.y;
		var itemBottom = itemTop + button.Height;
		if ( itemTop < viewportTop )
		{
			_scroll.VerticalScrollbar.Value -=
				(viewportTop - itemTop).CeilToInt();
		}
		else if ( itemBottom > viewportBottom )
		{
			_scroll.VerticalScrollbar.Value +=
				(itemBottom - viewportBottom).CeilToInt();
		}
	}

	private string SelectedItem() =>
		!string.IsNullOrWhiteSpace( _controller.Document.Workspace.SelectedControl )
			? _controller.Document.Workspace.SelectedControl
			: _controller.Document.Workspace.SelectedBone;

	private void UpdateControlLabels()
	{
		var labels = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
		{
			["@primary_hand"] =
				$"Primary hand · {BoundText( _controller.Document.Binding.PrimaryHand )}",
			["@support_hand"] =
				$"Support hand · {BoundText( _controller.Document.Binding.SupportHand )}",
			["@primary_elbow"] = "Primary elbow",
			["@support_elbow"] = "Support elbow"
		};
		foreach ( var item in labels )
		{
			if ( _itemButtons.TryGetValue( item.Key, out var button ) )
				button.Text = item.Value;
		}

		foreach ( var bone in HostSkeletonBuilder.BuildCached( _controller.Document )
			.Bones.Where( x => x.IsWeaponBone ) )
		{
			if ( !_itemButtons.TryGetValue( bone.Name, out var button ) )
				continue;
			button.Icon = _controller.GetVisibilityPart( bone.Name ) is null
				? ""
				: "visibility";
		}
	}

	internal static string StructureSignature( HostSkeleton skeleton ) =>
		string.Join(
			"|",
			skeleton.Bones.Select( bone =>
				$"{bone.Name}>{bone.ParentName}:{bone.IsWeaponBone}" ) );

	private static string BoundText( RigTarget target ) => target.IsBound ? "bound" : "unbound";
}

public sealed partial class SelectedControlInspectorPanel : Widget
{
	private readonly WeaponAnimatorController _controller;
	private readonly Label _type;
	private readonly Label _name;
	private readonly Label _details;
	private readonly Label _keyState;
	private readonly Widget _identity;
	private readonly Widget _identitySpine;
	private readonly Widget _transform;
	private Widget _checklist = null!;
	private readonly List<Action> _refreshers = [];
	private bool _checklistExpanded;
	private bool _rebuildingTransformFields;
	private bool _refreshingTransformFields;
	private bool _lastLocalGizmos;
	private int _transformFieldGeneration;

	public event Action<string, ValidationSeverity>? StatusChanged;

	public SelectedControlInspectorPanel(
		WeaponAnimatorController controller,
		Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		_identity = new Widget( this );
		_identity.Layout = Layout.Row();
		_identity.Layout.Margin = 0;
		_identity.Layout.Spacing = 0;
		_identity.SetStyles(
			"background-color: rgb(25,28,32); border: none; border-bottom: 1px solid rgba(255,255,255,0.07); border-radius: 0px;" );
		_identitySpine = new Widget( _identity ) { FixedWidth = 3 };
		_identitySpine.SetStyles(
			$"background-color: {WeaponAnimatorTheme.Cyan.Hex}; border: none; border-radius: 0px;" );
		_identity.Layout.Add( _identitySpine );
		var identityContent = new Widget( _identity );
		identityContent.Layout = Layout.Column();
		identityContent.Layout.Margin = new Sandbox.UI.Margin( 12, 11, 14, 11 );
		identityContent.Layout.Spacing = 3;
		_type = WeaponAnimatorTheme.SectionLabel( "NO SELECTION", identityContent, WeaponAnimatorTheme.Cyan );
		_name = WeaponAnimatorTheme.Label( "Select a control or bone", identityContent );
		_name.SetStyles(
			"background-color: transparent; border: none; padding: 0px;" +
			$"font-size: 17px; font-weight: 500; color: {WeaponAnimatorTheme.Text.Hex};" );
		_details = WeaponAnimatorTheme.Label( "Choose an item in the rig browser.", identityContent, true );
		_keyState = WeaponAnimatorTheme.Label( "No key", identityContent, true );
		identityContent.Layout.Add( _type );
		identityContent.Layout.Add( _name );
		identityContent.Layout.Add( _details );
		identityContent.Layout.Add( _keyState );
		_identity.Layout.Add( identityContent, 1 );
		Layout.Add( _identity );

		Layout.Add( BuildChecklist() );
		_transform = new Widget( this );
		_transform.Layout = Layout.Column();
		_transform.Layout.Margin = new Sandbox.UI.Margin( 10, 8, 10, 8 );
		_transform.Layout.Spacing = 6;
		Layout.Add( _transform );

		var tools = new AnimationInspectorPanel( controller, this, controlToolsOnly: true );
		tools.StatusChanged += ( message, severity ) => StatusChanged?.Invoke( message, severity );
		Layout.Add( tools, 1 );
		_controller.SelectionChanged += RebuildTransform;
		_controller.DocumentChanged += Refresh;
		_controller.PoseChanged += RefreshPose;
		_controller.TimelineChanged += Refresh;
		RebuildTransform();
	}

	public override void OnDestroyed()
	{
		_controller.SelectionChanged -= RebuildTransform;
		_controller.DocumentChanged -= Refresh;
		_controller.PoseChanged -= RefreshPose;
		_controller.TimelineChanged -= Refresh;
		base.OnDestroyed();
	}

	private Widget BuildChecklist()
	{
		_checklist = new Widget( this );
		_checklist.Layout = Layout.Column();
		_checklist.Layout.Margin = new Sandbox.UI.Margin( 10, 7, 10, 7 );
		_checklist.Layout.Spacing = 4;
		_checklist.SetStyles(
			"background-color: rgb(22,25,28); border: none; border-bottom: 1px solid rgba(255,255,255,0.06);" );
		RebuildChecklist();
		return _checklist;
	}

	private void RebuildChecklist()
	{
		if ( _checklist is null || !_checklist.IsValid() )
			return;
		_checklist.Layout.Clear( true );
		_checklist.Visible = !_controller.Document.Binding.ChecklistDismissed;
		if ( !_checklist.Visible )
			return;

		var states = ChecklistStates();
		var complete = states.Count( x => x.Complete );
		var next = states.FirstOrDefault( x => !x.Complete );
		var header = RigAuditPanel.Row( _checklist );
		var toggle = new WeaponAnimatorButton(
			$"{complete}/5  Grip setup{(next.Label is null ? " · Complete" : $" · Next: {next.Label}")}",
			"checklist",
			header )
		{
			Clicked = () =>
			{
				_checklistExpanded = !_checklistExpanded;
				RebuildChecklist();
			},
			Tint = WeaponAnimatorTheme.Surface
		};
		header.Layout.Add( toggle, 1 );
		var dismiss = new WeaponAnimatorButton( "", "close", header )
		{
			Clicked = () => _controller.Mutate(
				"Dismiss binding checklist",
				document => document.Binding.ChecklistDismissed = true ),
			Tint = WeaponAnimatorTheme.Surface,
			ToolTip = "Dismiss setup guide"
		};
		dismiss.FixedWidth = 32;
		header.Layout.Add( dismiss );
		_checklist.Layout.Add( header );
		if ( !_checklistExpanded )
			return;

		foreach ( var state in states )
		{
			var captured = state;
			_checklist.Layout.Add( new WeaponAnimatorButton(
				$"{(captured.Complete ? "✓" : "○")}  {captured.Label}",
				_checklist )
			{
				Clicked = captured.Select,
				Tint = captured.Complete
					? WeaponAnimatorTheme.Green * 0.22f
					: WeaponAnimatorTheme.Surface
			} );
		}
	}

	private List<(string? Label, bool Complete, Action Select)> ChecklistStates()
	{
		var document = _controller.Document;
		var clip = document.GetSelectedClip();
		var hasElbow = clip?.Tracks.Any( x =>
			(x.Target is "@primary_elbow" or "@support_elbow") && x.Keys.Count > 0 ) == true;
		var hasFinger = clip?.Tracks.Any( x =>
			x.Target.Contains( "finger", StringComparison.OrdinalIgnoreCase ) && x.Keys.Count > 0 ) == true;
		return
		[
			("Bind primary hand", document.Binding.PrimaryHand.IsBound, () => _controller.SelectControl( "@primary_hand" )),
			("Bind support hand",
				document.Binding.Configuration == GripConfiguration.OneHanded
					|| document.Binding.SupportHand.IsBound,
				() => _controller.SelectControl( "@support_hand" )),
			("Adjust elbow poles", hasElbow, () => _controller.SelectControl( "@primary_elbow" )),
			("Pose fingers", hasFinger, () => _controller.SelectBone( "finger_index_0_R" )),
			("Save default grip pose",
				document.Binding.GripPoses.Count > 0,
				() => _controller.SelectControl( "@primary_hand" ))
		];
	}

	private void RebuildTransform()
	{
		_rebuildingTransformFields = true;
		_lastLocalGizmos = _controller.Document.Workspace.LocalGizmos;
		_transformFieldGeneration++;
		try
		{
			RebuildChecklist();
			_transform.Layout.Clear( true );
			_refreshers.Clear();
			var context = SelectionTransformContext.Resolve( _controller );
			if ( context is null )
			{
				RefreshIdentity( null );
				return;
			}

			RefreshIdentity( context );
			var mode = RigAuditPanel.Row( _transform );
			mode.Layout.Add( WeaponAnimatorTheme.SectionLabel(
				"TRANSFORM",
				mode,
				context.Kind == RigControlKind.Weapon
					? WeaponAnimatorTheme.Amber
					: WeaponAnimatorTheme.Cyan ) );
			mode.Layout.AddStretchCell();
			mode.Layout.Add( ToggleAutoKey( mode ) );
			_transform.Layout.Add( mode );

			var space = context.LocalSpace ? "Local" : "World";
			AddVectorRow( $"{space} Position", 0.05f, context, TransformPart.Position );
			AddVectorRow( $"{space} Rotation", 0.5f, context, TransformPart.Rotation );
			AddVectorRow( $"{space} Scale", 0.005f, context, TransformPart.Scale );

			var actions = RigAuditPanel.Row( _transform );
			actions.Layout.Add( WeaponAnimatorTheme.Button(
				"Key pose",
				"diamond",
				() =>
				{
					var current = SelectionTransformContext.Resolve( _controller );
					if ( current is not null )
						_controller.CommitWorkingPose(
							current.Target,
							current.Kind,
							current.LocalTransform );
				},
				actions,
				true ) );
			actions.Layout.Add( WeaponAnimatorTheme.Button(
				"Revert",
				"restart_alt",
				() =>
				{
					var current = SelectionTransformContext.Resolve( _controller );
					if ( current is not null )
						_controller.DiscardWorkingPose( current.Target );
				},
				actions ) );
			if ( !context.Target.StartsWith( "@", StringComparison.Ordinal ) )
			{
				actions.Layout.Add( WeaponAnimatorTheme.Button(
					"Reset bind",
					"settings_backup_restore",
					() =>
					{
						var current = SelectionTransformContext.Resolve( _controller );
						var skeleton = HostSkeletonBuilder.BuildCached( _controller.Document );
						if ( current is null
							|| !skeleton.ByName.TryGetValue( current.Target, out var bone ) )
							return;
						_controller.ApplyTransformEdit(
							current.Target,
							current.Kind,
							skeleton.GetBindLocal( bone ) );
					},
					actions ) );
			}
			_transform.Layout.Add( actions );
			if ( context.Kind == RigControlKind.Weapon )
				AddVisibilityEditor( context );
			Refresh();
		}
		finally
		{
			_rebuildingTransformFields = false;
		}
	}

	private Button ToggleAutoKey( Widget parent )
	{
		var button = new WeaponAnimatorButton( "Auto-key", "fiber_manual_record", parent )
		{
			IsToggle = true,
			IsChecked = _controller.Document.Workspace.AutoKey,
			Tint = _controller.Document.Workspace.AutoKey
				? WeaponAnimatorTheme.Coral * 0.45f
				: WeaponAnimatorTheme.SurfaceRaised
		};
		button.Toggled = () =>
		{
			_controller.Mutate(
				"Auto-key",
				document => document.Workspace.AutoKey = button.IsChecked );
			RebuildTransform();
		};
		return button;
	}

	private void AddVectorRow(
		string label,
		float sensitivity,
		SelectionTransformContext initial,
		TransformPart part )
	{
		var row = RigAuditPanel.Row( _transform );
		var title = WeaponAnimatorTheme.Label( label, row, true );
		title.FixedWidth = 92;
		row.Layout.Add( title );
		var edits = new LineEdit[3];
		var fieldGeneration = _transformFieldGeneration;
		var axes = new[] { "X", "Y", "Z" };
		var colors = new[]
		{
			WeaponAnimatorTheme.Coral,
			WeaponAnimatorTheme.Green,
			new Color( 0.30f, 0.56f, 0.96f )
		};

		for ( var index = 0; index < 3; index++ )
		{
			var captured = index;
			var field = new Widget( row )
			{
				MinimumWidth = 76,
				FixedHeight = 26,
				Layout = Layout.Row()
			};
			field.Layout.Margin = 0;
			field.Layout.Spacing = 0;
			var edit = new LineEdit( field ) { FixedHeight = 26 };
			edit.SetStyles( WeaponAnimatorTheme.InputStyle );
			edit.EditingFinished += () =>
			{
				var current = SelectionTransformContext.Resolve( _controller );
				if ( !CanApplyFieldEdit(
					_rebuildingTransformFields,
					_refreshingTransformFields,
					fieldGeneration,
					_transformFieldGeneration,
					initial.Target,
					initial.Kind,
					current ) )
					return;
				if ( !float.TryParse(
					edit.Text,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var value )
					|| !WeaponAnimationMath.IsFinite( value ) )
					return;
				ApplyAxisValue( part, captured, value, false );
			};
			field.Layout.Add( new ScrubHandle(
				axes[captured],
				colors[captured],
				sensitivity,
				() => GetVector(
					SelectionTransformContext.Resolve( _controller )?.DisplayTransform
						?? initial.DisplayTransform,
					part )[captured],
				() => _controller.BeginContinuousEdit( $"{label} {axes[captured]}" ),
				value => ApplyAxisValue( part, captured, value, true ),
				_controller.EndContinuousEdit,
				field ) );
			field.Layout.Add( edit, 1 );
			row.Layout.Add( field, 1 );
			edits[index] = edit;
		}

		_refreshers.Add( () =>
		{
			var current = SelectionTransformContext.Resolve( _controller );
			if ( current is null )
				return;
			var vector = GetVector( current.DisplayTransform, part );
			for ( var index = 0; index < 3; index++ )
			{
				var text = vector[index].ToString( "0.###", CultureInfo.InvariantCulture );
				if ( edits[index].Text != text )
					edits[index].Value = text;
			}
		} );
		_transform.Layout.Add( row );
	}

	internal static bool CanApplyFieldEdit(
		bool rebuilding,
		bool refreshing,
		int fieldGeneration,
		int currentGeneration,
		string expectedTarget,
		RigControlKind expectedKind,
		SelectionTransformContext? current ) =>
		!rebuilding
		&& !refreshing
		&& fieldGeneration == currentGeneration
		&& current is not null
		&& current.Target.Equals( expectedTarget, StringComparison.OrdinalIgnoreCase )
		&& current.Kind == expectedKind;

	private void ApplyAxisValue(
		TransformPart part,
		int axis,
		float value,
		bool continuous )
	{
		var context = SelectionTransformContext.Resolve( _controller );
		if ( context is null )
			return;
		var displayed = context.DisplayTransform;
		var vector = GetVector( displayed, part );
		vector[axis] = part == TransformPart.Scale ? MathF.Max( value, 0.0001f ) : value;
		displayed = SetVector( displayed, part, vector );
		var local = context.ToLocal( displayed );
		if ( continuous )
			_controller.UpdateTransformEditContinuous( context.Target, context.Kind, local );
		else
			_controller.ApplyTransformEdit( context.Target, context.Kind, local );
	}

	private void Refresh()
	{
		if ( _lastLocalGizmos != _controller.Document.Workspace.LocalGizmos )
		{
			RebuildTransform();
			return;
		}

		RebuildChecklist();
		RefreshPose();
	}

	private void RefreshPose()
	{
		var context = SelectionTransformContext.Resolve( _controller );
		RefreshIdentity( context );
		_refreshingTransformFields = true;
		try
		{
			foreach ( var refresh in _refreshers )
				refresh();
		}
		finally
		{
			_refreshingTransformFields = false;
		}
	}

	private void RefreshIdentity( SelectionTransformContext? context )
	{
		if ( context is null )
		{
			_identity.SetStyles(
				"background-color: rgb(25,28,32); border: none; border-bottom: 1px solid rgba(255,255,255,0.07); border-radius: 0px;" );
			_identitySpine.SetStyles(
				$"background-color: {WeaponAnimatorTheme.Muted.Hex}; border: none; border-radius: 0px;" );
			_type.Text = "NO SELECTION";
			_name.Text = "Select a control or bone";
			_details.Text = "Choose an item in the rig browser.";
			_keyState.Text = "No key";
			return;
		}

		_type.Text = context.TypeName;
		var accent = context.Kind == RigControlKind.Weapon
			? WeaponAnimatorTheme.Amber
			: WeaponAnimatorTheme.Cyan;
		_type.Color = accent;
		_identitySpine.SetStyles(
			$"background-color: {accent.Hex}; border: none; border-radius: 0px;" );
		_identity.SetStyles(
			"background-color: rgb(25,28,32);" +
			"border: none; border-bottom: 1px solid rgba(255,255,255,0.07); border-radius: 0px;" );
		_name.Text = context.DisplayName;
		_details.Text = string.IsNullOrWhiteSpace( context.ParentName )
			? "No parent"
			: $"Parent: {context.ParentName}";
		var clip = _controller.Document.GetSelectedClip();
		var working = clip is not null
			&& _controller.Document.Workspace.GetWorkingPose( clip.Id, context.Target ) is not null;
		_keyState.Text = working
			? "◆ Unkeyed changes"
			: _controller.HasKeyAtPlayhead( context.Target )
				? "◆ Keyed at playhead"
				: "◇ No key at playhead";
		_keyState.Color = working
			? WeaponAnimatorTheme.Amber
			: _controller.HasKeyAtPlayhead( context.Target )
				? WeaponAnimatorTheme.Cyan
				: WeaponAnimatorTheme.Muted;
	}

	private static Vector3 GetVector( Transform transform, TransformPart part ) => part switch
	{
		TransformPart.Position => transform.Position,
		TransformPart.Rotation => new Vector3(
			transform.Rotation.Angles().pitch,
			transform.Rotation.Angles().yaw,
			transform.Rotation.Angles().roll ),
		_ => transform.Scale
	};

	private static Transform SetVector(
		Transform transform,
		TransformPart part,
		Vector3 value ) => part switch
	{
		TransformPart.Position => transform.WithPosition( value ),
		TransformPart.Rotation => transform.WithRotation( Rotation.From( value.x, value.y, value.z ).Normal ),
		_ => transform.WithScale( value )
	};

	private enum TransformPart
	{
		Position,
		Rotation,
		Scale
	}
}

internal sealed class SelectionTransformContext
{
	public string Target { get; init; } = "";
	public string DisplayName { get; init; } = "";
	public string ParentName { get; init; } = "";
	public RigControlKind Kind { get; init; }
	public Transform LocalTransform { get; init; }
	public Transform WorldTransform { get; init; }
	public Transform? ParentTransform { get; init; }
	public bool LocalSpace { get; init; }
	public Transform DisplayTransform => LocalSpace ? LocalTransform : WorldTransform;
	public string TypeName => Target switch
	{
		"@primary_hand" or "@support_hand" => "HAND IK TARGET",
		"@primary_elbow" or "@support_elbow" => "ELBOW POLE",
		_ when Kind == RigControlKind.Weapon => "WEAPON BONE",
		_ when Kind == RigControlKind.Camera => "CAMERA BONE",
		_ => "ARM BONE"
	};

	public Transform ToLocal( Transform displayed ) =>
		LocalSpace || ParentTransform is null
			? displayed
			: ParentTransform.Value.ToLocal( displayed );

	public static SelectionTransformContext? Resolve( WeaponAnimatorController controller )
	{
		var document = controller.Document;
		var clip = document.GetSelectedClip();
		var skeleton = HostSkeletonBuilder.BuildCached( document );
		var pose = AnimationPoseEvaluator.Evaluate(
			document,
			skeleton,
			clip,
			document.Workspace.TimelineTime,
			includeWorkingPose: true );
		var control = document.Workspace.SelectedControl;
		if ( !string.IsNullOrWhiteSpace( control ) )
		{
			var target = control switch
			{
				"@primary_hand" => document.Binding.PrimaryHand,
				"@support_hand" => document.Binding.SupportHand,
				"@primary_elbow" => document.Binding.PrimaryElbowPole,
				"@support_elbow" => document.Binding.SupportElbowPole,
				_ => null
			};
			if ( target is null )
				return null;
			var local = clip is not null
				&& document.Workspace.GetWorkingPose( clip.Id, control ) is { } working
					? working.Transform
					: clip?.Tracks.FirstOrDefault( x =>
						x.Target.Equals( control, StringComparison.OrdinalIgnoreCase ) ) is { } track
						? WeaponAnimationMath.SampleTrack(
							track,
							document.Workspace.TimelineTime,
							target.Transform )
						: target.Transform;
			Transform? parent = null;
			if ( !string.IsNullOrWhiteSpace( target.AttachedBone )
				&& pose.Model.TryGetValue( target.AttachedBone, out var attached ) )
				parent = attached;
			var world = parent is null
				? local
				: new Transform(
					parent.Value.PointToWorld( local.Position ),
					parent.Value.Rotation * local.Rotation,
					parent.Value.Scale * local.Scale );
			return new SelectionTransformContext
			{
				Target = control,
				DisplayName = target.Name,
				ParentName = target.AttachedBone,
				Kind = RigControlKind.Arm,
				LocalTransform = local,
				WorldTransform = world,
				ParentTransform = parent,
				LocalSpace = document.Workspace.LocalGizmos
			};
		}

		var selected = document.Workspace.SelectedBone;
		if ( string.IsNullOrWhiteSpace( selected )
			|| !skeleton.ByName.TryGetValue( selected, out var bone )
			|| !pose.Local.TryGetValue( selected, out var boneLocal )
			|| !pose.Model.TryGetValue( selected, out var boneWorld ) )
			return null;
		Transform? boneParent = null;
		if ( !string.IsNullOrWhiteSpace( bone.ParentName )
			&& pose.Model.TryGetValue( bone.ParentName, out var parentModel ) )
			boneParent = parentModel;
		return new SelectionTransformContext
		{
			Target = selected,
			DisplayName = selected,
			ParentName = bone.ParentName,
			Kind = bone.IsWeaponBone ? RigControlKind.Weapon : RigControlKind.Arm,
			LocalTransform = boneLocal,
			WorldTransform = boneWorld,
			ParentTransform = boneParent,
			LocalSpace = document.Workspace.LocalGizmos
		};
	}
}
