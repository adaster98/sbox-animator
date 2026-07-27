#nullable enable annotations

using System;
using System.Globalization;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed partial class SelectedControlInspectorPanel
{
	private void AddVisibilityEditor( SelectionTransformContext context )
	{
		var header = RigAuditPanel.Row( _transform );
		header.Layout.Add( WeaponAnimatorTheme.SectionLabel(
			"VISIBILITY",
			header,
			WeaponAnimatorTheme.Amber ) );
		header.Layout.AddStretchCell();
		_transform.Layout.Add( header );

		var part = _controller.GetVisibilityPart( context.Target );
		if ( part is null )
		{
			var description = WeaponAnimatorTheme.Label(
				"Make this bone branch visible or hidden at keyed moments.",
				_transform,
				true );
			description.WordWrap = true;
			_transform.Layout.Add( description );
			_transform.Layout.Add( WeaponAnimatorTheme.Button(
				"Enable visibility keys",
				"visibility",
				() =>
				{
					_controller.AddVisibilityPart( context.Target );
					RebuildTransform();
				},
				_transform,
				true ) );
			return;
		}

		var nameRow = RigAuditPanel.Row( _transform );
		var nameLabel = WeaponAnimatorTheme.Label( "Part name", nameRow, true );
		nameLabel.FixedWidth = 92;
		nameRow.Layout.Add( nameLabel );
		var name = new LineEdit( nameRow )
		{
			Text = part.Name,
			FixedHeight = 26
		};
		name.SetStyles( WeaponAnimatorTheme.InputStyle );
		name.EditingFinished += () =>
		{
			var value = name.Text.Trim();
			if ( string.IsNullOrWhiteSpace( value ) || value == part.Name )
				return;
			_controller.UpdateVisibilityPart(
				part.Id,
				$"Rename {part.Name}",
				x => x.Name = value );
			RebuildTransform();
		};
		nameRow.Layout.Add( name, 1 );
		_transform.Layout.Add( nameRow );

		var keyRow = RigAuditPanel.Row( _transform );
		var state = new WeaponAnimatorButton( "", "visibility", keyRow );
		state.Clicked = () =>
			_controller.UpsertVisibilityKey(
				part.Id,
				!_controller.EvaluateVisibility( part.Id ) );
		keyRow.Layout.Add( state, 1 );
		var removeKey = new WeaponAnimatorButton( "", "key_off", keyRow )
		{
			Clicked = () => _controller.RemoveVisibilityKeyAtPlayhead( part.Id ),
			ToolTip = "Remove the visibility key at the playhead"
		};
		removeKey.FixedWidth = 34;
		keyRow.Layout.Add( removeKey );
		_transform.Layout.Add( keyRow );

		var defaults = RigAuditPanel.Row( _transform );
		var defaultButton = new WeaponAnimatorButton( "", "first_page", defaults );
		defaultButton.Clicked = () =>
			_controller.UpdateVisibilityPart(
				part.Id,
				$"Change {part.Name} default visibility",
				x => x.DefaultVisible = !x.DefaultVisible );
		defaults.Layout.Add( defaultButton, 1 );
		var mode = new WeaponAnimatorButton( "", "expand_more", defaults )
		{
			ToolTip = "How this part is hidden in preview and generated playback"
		};
		mode.Clicked = () => ShowVisibilityModeMenu( part, mode );
		defaults.Layout.Add( mode, 1 );
		_transform.Layout.Add( defaults );

		if ( part.RenderMode == VisibilityRenderMode.BodyGroup )
			AddBodyGroupFields( part );
		else
		{
			var note = WeaponAnimatorTheme.Label(
				"Bone branch mode works best when this bone exclusively drives the part's vertices.",
				_transform,
				true );
			note.WordWrap = true;
			_transform.Layout.Add( note );
		}

		var remove = WeaponAnimatorTheme.Button(
			"Remove visibility channel",
			"delete",
			() =>
			{
				_controller.RemoveVisibilityPart( part.Id );
				RebuildTransform();
			},
			_transform );
		remove.Tint = WeaponAnimatorTheme.Coral * 0.30f;
		_transform.Layout.Add( remove );

		_refreshers.Add( () =>
		{
			var visible = _controller.EvaluateVisibility( part.Id );
			state.Text = visible ? "Visible at playhead" : "Hidden at playhead";
			state.Icon = visible ? "visibility" : "visibility_off";
			state.Tint = visible
				? WeaponAnimatorTheme.Green * 0.34f
				: WeaponAnimatorTheme.Coral * 0.38f;
			removeKey.Visible = _controller.HasVisibilityKeyAtPlayhead( part.Id );
			defaultButton.Text = part.DefaultVisible
				? "Default: Visible"
				: "Default: Hidden";
			defaultButton.Icon = part.DefaultVisible ? "visibility" : "visibility_off";
			defaultButton.Tint = part.DefaultVisible
				? WeaponAnimatorTheme.Green * 0.20f
				: WeaponAnimatorTheme.Coral * 0.22f;
			mode.Text = part.RenderMode == VisibilityRenderMode.BoneBranch
				? "Bone branch"
				: "Bodygroup";
		} );
	}

	private void ShowVisibilityModeMenu(
		WeaponVisibilityPart part,
		WeaponAnimatorButton button )
	{
		var menu = new Menu( button );
		menu.AddOption( "Bone branch", "account_tree", () =>
		{
			_controller.UpdateVisibilityPart(
				part.Id,
				$"Use bone visibility for {part.Name}",
				x => x.RenderMode = VisibilityRenderMode.BoneBranch );
			RebuildTransform();
		} );
		menu.AddOption( "Bodygroup", "layers", () =>
		{
			_controller.UpdateVisibilityPart(
				part.Id,
				$"Use bodygroup visibility for {part.Name}",
				x => x.RenderMode = VisibilityRenderMode.BodyGroup );
			RebuildTransform();
		} );
		menu.OpenAt( button.ScreenRect.BottomLeft );
	}

	private void AddBodyGroupFields( WeaponVisibilityPart part )
	{
		var groupRow = RigAuditPanel.Row( _transform );
		var label = WeaponAnimatorTheme.Label( "Bodygroup", groupRow, true );
		label.FixedWidth = 92;
		groupRow.Layout.Add( label );
		var group = new LineEdit( groupRow )
		{
			Text = part.BodyGroupName,
			PlaceholderText = "group name",
			FixedHeight = 26
		};
		group.SetStyles( WeaponAnimatorTheme.InputStyle );
		group.EditingFinished += () =>
			_controller.UpdateVisibilityPart(
				part.Id,
				$"Set {part.Name} bodygroup",
				x => x.BodyGroupName = group.Text.Trim() );
		groupRow.Layout.Add( group, 1 );
		_transform.Layout.Add( groupRow );

		var values = RigAuditPanel.Row( _transform );
		var valueLabel = WeaponAnimatorTheme.Label( "Values", values, true );
		valueLabel.FixedWidth = 92;
		values.Layout.Add( valueLabel );
		values.Layout.Add( BodyGroupValueField(
			part.VisibleBodyGroupValue,
			"visible",
			value => _controller.UpdateVisibilityPart(
				part.Id,
				$"Set {part.Name} visible bodygroup value",
				x => x.VisibleBodyGroupValue = value ),
			values ), 1 );
		values.Layout.Add( BodyGroupValueField(
			part.HiddenBodyGroupValue,
			"hidden",
			value => _controller.UpdateVisibilityPart(
				part.Id,
				$"Set {part.Name} hidden bodygroup value",
				x => x.HiddenBodyGroupValue = value ),
			values ), 1 );
		_transform.Layout.Add( values );
	}

	private static LineEdit BodyGroupValueField(
		int value,
		string placeholder,
		Action<int> changed,
		Widget parent )
	{
		var field = new LineEdit( parent )
		{
			Text = value.ToString( CultureInfo.InvariantCulture ),
			PlaceholderText = placeholder,
			FixedHeight = 26
		};
		field.SetStyles( WeaponAnimatorTheme.InputStyle );
		field.EditingFinished += () =>
		{
			if ( int.TryParse(
				field.Text,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var parsed ) )
				changed( parsed );
		};
		return field;
	}
}
