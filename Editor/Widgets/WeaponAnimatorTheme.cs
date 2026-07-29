#nullable enable annotations

using System;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class WeaponAnimatorTheme
{
	public static readonly Color Background = new( 0.052f, 0.058f, 0.064f );
	public static readonly Color Surface = new( 0.082f, 0.091f, 0.101f );
	public static readonly Color SurfaceRaised = new( 0.105f, 0.115f, 0.126f );
	public static readonly Color Border = Color.White.WithAlpha( 0.075f );
	public static readonly Color Text = new( 0.88f, 0.90f, 0.92f );
	public static readonly Color Muted = new( 0.52f, 0.56f, 0.61f );
	public static readonly Color Cyan = new( 0.15f, 0.78f, 0.91f );
	public static readonly Color Amber = new( 0.96f, 0.61f, 0.16f );
	public static readonly Color Green = new( 0.34f, 0.82f, 0.50f );
	public static readonly Color Coral = new( 0.98f, 0.38f, 0.34f );
	public const float ScrollbarGutter = 14;

	/// <summary>
	/// Arm bone depth ramp, root to fingertips. Saturation stays high across the whole arc - an
	/// earlier version faded toward white at the fingertips, and desaturated colours collapse
	/// together against the dark viewport, which is exactly where the bones are densest. Hue
	/// carries the signal instead, sweeping violet through cyan to chartreuse, staying clear of
	/// Amber (weapon bones) and Coral (IK bones).
	/// </summary>
	private static readonly Color[] BoneDepthRamp =
	[
		new( 0.58f, 0.24f, 1.00f ),
		new( 0.30f, 0.45f, 1.00f ),
		new( 0.08f, 0.68f, 1.00f ),
		new( 0.10f, 0.92f, 0.94f ),
		new( 0.16f, 1.00f, 0.58f ),
		new( 0.52f, 1.00f, 0.30f ),
		new( 0.82f, 1.00f, 0.24f )
	];

	/// <summary>
	/// Samples the bone depth ramp. <paramref name="fraction"/> is 0 at the skeleton root and 1 at
	/// the deepest bone.
	/// </summary>
	public static Color BoneDepthColor( float fraction )
	{
		if ( !float.IsFinite( fraction ) )
			return BoneDepthRamp[0];

		var clamped = Math.Clamp( fraction, 0, 1 );
		var scaled = clamped * (BoneDepthRamp.Length - 1);
		var index = Math.Clamp( (int)scaled, 0, BoneDepthRamp.Length - 2 );
		return Color.Lerp(
			BoneDepthRamp[index],
			BoneDepthRamp[index + 1],
			scaled - index );
	}

	public const string PanelStyle =
		"background-color: rgb(21,23,26);" +
		"border: 1px solid rgba(255,255,255,0.075);" +
		"border-radius: 3px;";

	public const string InputStyle =
		"background-color: rgb(13,15,17);" +
		"border: 1px solid rgba(255,255,255,0.09);" +
		"border-radius: 3px;" +
		"color: rgb(224,229,234);" +
		"selection-background-color: rgb(31,126,151);" +
		"padding: 0 7px;" +
		"font-size: 11px;";

	public static Sandbox.UI.Margin ScrollCanvasMargin( float padding = 0 ) =>
		new( padding, padding, padding + ScrollbarGutter, padding );

	public static Button Button(
		string text,
		string icon,
		System.Action clicked,
		Widget? parent = null,
		bool primary = false )
	{
		var button = new WeaponAnimatorButton( text, icon, parent )
		{
			Clicked = clicked,
			FixedHeight = 28,
			Tint = primary ? Cyan * 0.65f : SurfaceRaised,
			ToolTip = text
		};
		return button;
	}

	public static Label Label( string text, Widget parent = null, bool muted = false )
	{
		var label = new Label( text, parent )
		{
			Color = muted ? Muted : Text
		};
		label.SetStyles(
			"background-color: transparent; border: none; padding: 0px;" +
			$"font-size: 11px; color: {(muted ? Muted : Text).Hex};" );
		return label;
	}

	public static Label SectionLabel(
		string text,
		Widget parent,
		Color? color = null,
		bool topMargin = false )
	{
		var label = new Label( text, parent )
		{
			Color = color ?? Muted
		};
		label.SetStyles(
			"background-color: transparent; border: none; padding: 0px;" +
			$"font-size: 9px; font-weight: 600; letter-spacing: 0.65px; color: {(color ?? Muted).Hex};" +
			(topMargin ? "margin-top: 7px;" : "") );
		return label;
	}
}

public sealed class WeaponAnimatorButton : Button
{
	public bool Flat { get; set; }

	public WeaponAnimatorButton( string text, Widget? parent = null ) : base( text, parent )
	{
		ToolTip = text;
	}

	public WeaponAnimatorButton( string text, string icon, Widget? parent = null )
		: base( text, icon, parent )
	{
		ToolTip = text;
	}

	protected override Vector2 SizeHint() => PreferredSize();
	protected override Vector2 MinimumSizeHint() => PreferredSize();
	public float PreferredWidth => PreferredSize().x;

	public void FitToContent( bool fixedWidth = false )
	{
		var width = MathF.Ceiling( PreferredWidth );
		MinimumWidth = width;
		if ( fixedWidth )
			FixedWidth = width;
		Update();
	}

	private Vector2 PreferredSize()
	{
		Paint.SetDefaultFont();
		var hasIcon = !string.IsNullOrWhiteSpace( Icon );
		var textWidth = string.IsNullOrWhiteSpace( Text ) ? 0 : Paint.MeasureText( Text ).x;
		var content = ContentLayout( 0, textWidth, hasIcon );
		return new Vector2(
			MathF.Max( 36, 20 + content.IconWidth + content.Gap + textWidth ),
			28 );
	}

	internal static (float StartX, float IconWidth, float Gap) ContentLayout(
		float centerX,
		float textWidth,
		bool hasIcon )
	{
		const float iconSize = 15;
		const float spacing = 4;
		var hasText = textWidth > 0;
		var gap = hasIcon && hasText ? spacing : 0;
		var iconWidth = hasIcon ? iconSize : 0;
		var contentWidth = textWidth + iconWidth + gap;
		return (centerX - contentWidth * 0.5f, iconWidth, gap);
	}

	protected override void OnPaint()
	{
		var color = Tint.ToHsv();
		var background = color;
		if ( Flat )
		{
			background = Color.Transparent;
			color = Enabled
				? color
				: Theme.SurfaceLightBackground.WithAlpha( 0.35f );
			if ( Enabled && Paint.HasMouseOver )
				color = color with { Value = MathF.Min( color.Value + 0.18f, 1.0f ) };
		}
		else if ( Enabled )
		{
			if ( Paint.HasPressed )
				background = color with { Value = color.Value + 0.1f };
			else if ( Paint.HasMouseOver )
				background = color with { Value = color.Value + 0.2f };
		}
		else
		{
			background = color = Theme.SurfaceLightBackground;
		}

		if ( !Flat && (!Enabled || ReadOnly) )
		{
			color = color.WithSaturation( 0.1f ).WithAlpha( 0.5f );
			background = color.WithAlpha( 0.2f );
		}

		if ( background.Alpha > 0 )
		{
			Paint.Antialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( background with
			{
				Value = background.Value + 0.04f,
				Saturation = color.Saturation * 0.8f
			} );
			Paint.DrawRect( LocalRect, 3 );
			Paint.SetBrushLinear(
				LocalRect.TopLeft,
				LocalRect.BottomRight,
				background,
				background with { Value = background.Value - 0.03f } );
			Paint.DrawRect( LocalRect.Shrink( 1 ), 3 );
		}
		else if ( !Flat )
		{
			color = Color.White.WithAlpha( 0.5f );
		}

		Paint.SetDefaultFont();
		Paint.SetPen( color with { Value = 0.99f, Saturation = color.Saturation * 0.20f } );

		const float iconSize = 15;
		var hasIcon = !string.IsNullOrWhiteSpace( Icon );
		var displayedText = Text ?? "";
		var measuredText = string.IsNullOrEmpty( displayedText )
			? Vector2.Zero
			: Paint.MeasureText( displayedText );
		var content = ContentLayout(
			LocalRect.Center.x,
			measuredText.x,
			hasIcon );
		var cursorX = content.StartX;

		if ( hasIcon )
		{
			Paint.DrawIcon(
				new Rect( cursorX, LocalRect.Center.y - iconSize * 0.5f, iconSize, iconSize ),
				Icon,
				iconSize );
			cursorX += content.IconWidth + content.Gap;
		}

		if ( measuredText.x > 0 )
		{
			Paint.DrawText(
				new Rect( cursorX, LocalRect.Top, measuredText.x, LocalRect.Height ),
				displayedText,
				TextFlag.Center );
		}
	}
}

public sealed class PanelChrome : Widget
{
	public Widget Body { get; }
	public Label TitleLabel { get; }
	public Label StatusLabel { get; }

	public PanelChrome( string title, string icon, Widget? content = null, Widget? parent = null )
		: base( parent )
	{
		SetStyles( WeaponAnimatorTheme.PanelStyle );
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		var header = new Widget( this )
		{
			FixedHeight = 34
		};
		header.SetStyles(
			"background-color: rgb(27,30,34); border: none;" );
		header.Layout = Layout.Row();
		header.Layout.Margin = new Sandbox.UI.Margin( 10, 0, 10, 0 );
		header.Layout.Spacing = 7;

		var iconLabel = new Label( icon, header );
		iconLabel.SetStyles(
			"background-color: transparent; border: none; padding: 0px;" +
			$"font-family: Material Icons; font-size: 15px; color: {WeaponAnimatorTheme.Cyan.Hex};" );
		iconLabel.FixedWidth = 18;
		header.Layout.Add( iconLabel );

		TitleLabel = WeaponAnimatorTheme.Label( title, header );
		TitleLabel.SetStyles(
			"background-color: transparent; border: none; padding: 0px;" +
			$"font-size: 10px; font-weight: 600; letter-spacing: 0.65px; color: {WeaponAnimatorTheme.Text.Hex};" );
		header.Layout.Add( TitleLabel );
		header.Layout.AddStretchCell();

		StatusLabel = WeaponAnimatorTheme.Label( "", header, true );
		header.Layout.Add( StatusLabel );
		Layout.Add( header );
		var separator = new Widget( this ) { FixedHeight = 1 };
		separator.SetStyles( "background-color: rgba(255,255,255,0.07); border: none;" );
		Layout.Add( separator );

		Body = content ?? new Widget( this );
		Body.SetStyles( "background-color: transparent; border: none;" );
		Body.Parent = this;
		Layout.Add( Body, 1 );
	}
}

public sealed class WeaponAnimatorToolbar : Widget
{
	private readonly Widget _left;
	private readonly Widget _center;
	private readonly Widget _right;
	private readonly System.Collections.Generic.List<ToolbarAction> _leftActions = [];
	private WeaponAnimatorButton? _overflowButton;

	public WeaponAnimatorToolbar( Widget? parent = null ) : base( parent )
	{
		FixedHeight = 48;
		SetStyles(
			"background-color: rgb(18,20,23);" +
			"border-bottom: 1px solid rgba(255,255,255,0.08);" );
		var grid = Layout.Grid();
		grid.Margin = new Sandbox.UI.Margin( 10, 8, 10, 8 );
		grid.HorizontalSpacing = 5;
		grid.SetColumnStretch( 1, 1, 1 );
		Layout = grid;

		_left = Section( this );
		_center = Section( this );
		_right = Section( this );
		grid.AddCell( 0, 0, _left, alignment: TextFlag.LeftCenter );
		grid.AddCell( 1, 0, _center, alignment: TextFlag.Center );
		grid.AddCell( 2, 0, _right, alignment: TextFlag.RightCenter );
	}

	public Button AddLeft(
		string text,
		string icon,
		System.Action clicked,
		bool primary = false,
		bool overflowAtNarrowWidth = false )
	{
		var button = AddButton( _left, text, icon, clicked, primary );
		_leftActions.Add( new ToolbarAction(
			(WeaponAnimatorButton)button,
			text,
			clicked,
			overflowAtNarrowWidth ) );
		return button;
	}

	public Button AddCenter( string text, string icon, System.Action clicked, bool primary = false ) =>
		AddButton( _center, text, icon, clicked, primary );

	public Button AddRight( string text, string icon, System.Action clicked, bool primary = false ) =>
		AddButton( _right, text, icon, clicked, primary );

	public void BalanceCenter()
	{
		EnsureOverflowButton();
		ApplyAvailableWidth( Width );
	}

	public void Clear()
	{
		_left.Layout.Clear( true );
		_center.Layout.Clear( true );
		_right.Layout.Clear( true );
		_left.MinimumWidth = 0;
		_center.MinimumWidth = 0;
		_right.MinimumWidth = 0;
		_leftActions.Clear();
		_overflowButton = null;
	}

	protected override void OnResize()
	{
		base.OnResize();
		ApplyAvailableWidth( Width );
	}

	internal void ApplyAvailableWidth( float availableWidth )
	{
		UpdateOverflow( availableWidth );
		var sideWidth = MathF.Max( ContentWidth( _left ), ContentWidth( _right ) );
		_left.MinimumWidth = sideWidth;
		_right.MinimumWidth = sideWidth;
	}

	private void EnsureOverflowButton()
	{
		if ( _overflowButton is not null || _leftActions.All( x => !x.OverflowAtNarrowWidth ) )
			return;

		_overflowButton = (WeaponAnimatorButton)AddButton(
			_left,
			"More",
			"more_horiz",
			ShowOverflowMenu,
			false );
		_overflowButton.Visible = false;
	}

	private void UpdateOverflow( float availableWidth )
	{
		if ( _overflowButton is null )
			return;

		var narrow = availableWidth > 0 && availableWidth < 1380;
		foreach ( var action in _leftActions.Where( x => x.OverflowAtNarrowWidth ) )
			action.Button.Visible = !narrow;
		_overflowButton.Visible = narrow;
	}

	internal bool UsesOverflow => _overflowButton?.Visible == true;

	private void ShowOverflowMenu()
	{
		if ( _overflowButton is null )
			return;

		var menu = new Menu( _overflowButton );
		foreach ( var action in _leftActions.Where( x => x.OverflowAtNarrowWidth ) )
			menu.AddOption( action.Text, null, action.Clicked );
		menu.OpenAt( _overflowButton.ScreenRect.BottomLeft );
	}

	private static float ContentWidth( Widget section ) =>
		section.Children
			.OfType<WeaponAnimatorButton>()
			.Where( x => x.Visible )
			.Sum( x => x.PreferredWidth + 5 );

	private static Button AddButton(
		Widget section,
		string text,
		string icon,
		System.Action clicked,
		bool primary )
	{
		var button = WeaponAnimatorTheme.Button( text, icon, clicked, section, primary );
		section.Layout.Add( button );
		if ( button is WeaponAnimatorButton animatorButton )
			animatorButton.FitToContent( true );
		return button;
	}

	private static Widget Section( Widget parent )
	{
		var section = new Widget( parent );
		section.SetStyles( "background-color: transparent; border: none;" );
		section.Layout = Layout.Row();
		section.Layout.Margin = 0;
		section.Layout.Spacing = 5;
		return section;
	}

	private sealed record ToolbarAction(
		WeaponAnimatorButton Button,
		string Text,
		System.Action Clicked,
		bool OverflowAtNarrowWidth );
}
