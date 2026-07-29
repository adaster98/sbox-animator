#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

internal sealed class CurveEditorToolbar : Widget
{
	private readonly TimelineEditorCanvas _timeline;
	private readonly LineEdit _search;
	private readonly WeaponAnimatorButton _speed;
	private readonly WeaponAnimatorButton _channels;
	private readonly Dictionary<TransformCurveChannel, WeaponAnimatorButton> _channelButtons = [];
	private bool _refreshing;

	public CurveEditorToolbar(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		_timeline = timeline;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 5, 3 );
		Layout.Spacing = 4;
		SetStyles(
			"background-color: rgb(19,22,25); border: none;" +
			"border-bottom: 1px solid rgba(255,255,255,0.055);" );

		_search = new LineEdit( this )
		{
			PlaceholderText = "Search keyed parts…",
			FixedWidth = TimelineEditorCanvas.CurveTrackListWidth - 10,
			FixedHeight = 26
		};
		_search.SetStyles( WeaponAnimatorTheme.InputStyle );
		_search.TextEdited += text =>
		{
			if ( _refreshing || _timeline.Clip is not { } clip )
				return;
			_timeline.Controller.SetCurveSearch( clip, text ?? "" );
		};
		Layout.Add( _search );

		Layout.Add( Separator( this ) );
		_speed = Toggle( "Speed", "speed", () => SetMode( CurveEditorMode.Speed ) );
		_channels = Toggle( "Channels", "tune", () => SetMode( CurveEditorMode.Channels ) );
		Layout.Add( _speed );
		Layout.Add( _channels );

		Layout.Add( Separator( this ) );
		foreach ( var (channel, label, color) in ChannelButtons )
		{
			var captured = channel;
			var button = new WeaponAnimatorButton( label, this )
			{
				IsToggle = true,
				FixedWidth = 31,
				FixedHeight = 26,
				Tint = color.WithAlpha( 0.32f ),
				ToolTip = ChannelName( channel )
			};
			button.Clicked = () => ToggleChannel( captured );
			_channelButtons[channel] = button;
			Layout.Add( button );
		}

		Layout.AddStretchCell();
		Layout.Add( Preset( "Linear", CurvePreset.Linear ) );
		Layout.Add( Preset( "Ease In", CurvePreset.EaseIn ) );
		Layout.Add( Preset( "Ease Out", CurvePreset.EaseOut ) );
		Layout.Add( Preset( "Ease In-Out", CurvePreset.EaseInOut ) );
		var fit = new WeaponAnimatorButton( "", "fit_screen", this )
		{
			FixedWidth = 30,
			FixedHeight = 26,
			Tint = WeaponAnimatorTheme.SurfaceRaised,
			ToolTip = "Fit visible curves (F)",
			Clicked = Fit
		};
		Layout.Add( fit );
	}

	public void Refresh()
	{
		var clip = _timeline.Clip;
		if ( clip is null )
			return;
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		_refreshing = true;
		if ( _search.Text != view.Search )
			_search.Text = view.Search;
		_refreshing = false;

		SetToggleState( _speed, view.Mode == CurveEditorMode.Speed );
		SetToggleState( _channels, view.Mode == CurveEditorMode.Channels );
		foreach ( var pair in _channelButtons )
		{
			pair.Value.Visible = view.Mode == CurveEditorMode.Channels;
			SetToggleState( pair.Value, (view.VisibleChannels & pair.Key) != 0 );
		}
	}

	private void SetMode( CurveEditorMode mode )
	{
		if ( _timeline.Clip is { } clip )
			_timeline.Controller.SetCurveMode( clip, mode );
	}

	private void ToggleChannel( TransformCurveChannel channel )
	{
		if ( _timeline.Clip is not { } clip )
			return;
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		var channels = view.VisibleChannels ^ channel;
		if ( channels == TransformCurveChannel.None )
			channels = channel;
		_timeline.Controller.SetCurveChannels( clip, channels );
	}

	private void Fit()
	{
		if ( _timeline.Clip is { } clip )
			_timeline.Controller.FitCurveVerticalRange( clip );
	}

	private WeaponAnimatorButton Toggle( string text, string icon, Action clicked )
	{
		var button = new WeaponAnimatorButton( text, icon, this )
		{
			IsToggle = true,
			FixedHeight = 26,
			Tint = WeaponAnimatorTheme.SurfaceRaised,
			Clicked = clicked
		};
		button.FitToContent( true );
		return button;
	}

	private WeaponAnimatorButton Preset( string text, CurvePreset preset )
	{
		var button = new WeaponAnimatorButton( text, this )
		{
			FixedHeight = 26,
			Tint = WeaponAnimatorTheme.SurfaceRaised,
			Clicked = () => _timeline.Controller.ApplyCurvePreset( preset )
		};
		button.FitToContent( true );
		return button;
	}

	private static Widget Separator( Widget parent )
	{
		var separator = new Widget( parent ) { FixedWidth = 1, FixedHeight = 18 };
		separator.SetStyles(
			"background-color: rgba(255,255,255,0.085); border: none; margin: 4px 3px;" );
		return separator;
	}

	private static void SetToggleState( WeaponAnimatorButton button, bool active )
	{
		button.IsChecked = active;
		button.Tint = active
			? WeaponAnimatorTheme.Cyan * 0.58f
			: WeaponAnimatorTheme.SurfaceRaised;
	}

	private static string ChannelName( TransformCurveChannel channel ) => channel switch
	{
		TransformCurveChannel.PositionX => "Position X",
		TransformCurveChannel.PositionY => "Position Y",
		TransformCurveChannel.PositionZ => "Position Z",
		TransformCurveChannel.RotationX => "Rotation X",
		TransformCurveChannel.RotationY => "Rotation Y",
		TransformCurveChannel.RotationZ => "Rotation Z",
		TransformCurveChannel.ScaleX => "Scale X",
		TransformCurveChannel.ScaleY => "Scale Y",
		_ => "Scale Z"
	};

	private static readonly (TransformCurveChannel Channel, string Label, Color Color)[]
		ChannelButtons =
		[
			(TransformCurveChannel.PositionX, "PX", new Color( 0.92f, 0.31f, 0.28f )),
			(TransformCurveChannel.PositionY, "PY", new Color( 0.36f, 0.82f, 0.43f )),
			(TransformCurveChannel.PositionZ, "PZ", new Color( 0.30f, 0.58f, 0.96f )),
			(TransformCurveChannel.RotationX, "RX", new Color( 1.00f, 0.50f, 0.33f )),
			(TransformCurveChannel.RotationY, "RY", new Color( 0.55f, 0.93f, 0.54f )),
			(TransformCurveChannel.RotationZ, "RZ", new Color( 0.42f, 0.72f, 1.00f )),
			(TransformCurveChannel.ScaleX, "SX", new Color( 0.95f, 0.56f, 0.72f )),
			(TransformCurveChannel.ScaleY, "SY", new Color( 0.65f, 0.90f, 0.71f )),
			(TransformCurveChannel.ScaleZ, "SZ", new Color( 0.62f, 0.77f, 0.98f ))
		];
}

internal sealed class CurveEditorArea : Widget
{
	private readonly CurveTrackList _tracks;
	private readonly CurveVerticalAxis _axis;
	private readonly CurveGraphWidget _graph;

	public CurveEditorArea(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		Layout = Layout.Row();
		Layout.Margin = 0;
		Layout.Spacing = 0;
		_tracks = new CurveTrackList( timeline, this )
		{
			FixedWidth = TimelineEditorCanvas.CurveTrackListWidth
		};
		_axis = new CurveVerticalAxis( timeline, this )
		{
			FixedWidth = TimelineEditorCanvas.CurveAxisWidth
		};
		_graph = new CurveGraphWidget( timeline, this );
		_axis.RangeProvider = _graph.ResolveVerticalRange;
		_graph.RangeChanged = _axis.Update;
		Layout.Add( _tracks );
		Layout.Add( _axis );
		Layout.Add( _graph, 1 );
	}

	public void Refresh()
	{
		_tracks.Refresh();
		_axis.Update();
		_graph.Refresh();
	}

	public void InvalidateData() => _graph.InvalidateData();

	public void InvalidateView() => _graph.InvalidateView();
}

internal sealed class CurveTrackList : BaseScrollWidget
{
	private const float RowHeight = TimelineEditorCanvas.TrackHeight;
	private readonly TimelineEditorCanvas _timeline;
	private Guid _restoredClipId;
	private string _restoredSearch = "";
	private bool _restoring;

	public CurveTrackList(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		_timeline = timeline;
		MouseTracking = true;
		HorizontalScrollbarMode = ScrollbarMode.Off;
		VerticalScrollbarMode = ScrollbarMode.Auto;
		SetStyles(
			"background-color: rgb(21,24,27); border: none;" +
			"border-right: 1px solid rgba(255,255,255,0.065);" );
	}

	public void Refresh()
	{
		ConfigureScroll();
		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();
		Paint.SetBrushAndPen( new Color( 0.082f, 0.091f, 0.101f ) );
		Paint.DrawRect( LocalRect );
		var clip = _timeline.Clip;
		if ( clip is null )
			return;
		ConfigureScroll();
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		var tracks = CurveEditingService.KeyedTracks( clip, view.Search );
		for ( var index = 0; index < tracks.Count; index++ )
		{
			var y = index * RowHeight - VerticalScrollbar.Value;
			if ( y + RowHeight < 0 || y > Height )
				continue;
			var selected = tracks[index].Id == view.SelectedTrackId;
			if ( selected )
			{
				Paint.SetBrushAndPen( TrackColor( tracks[index].Kind ).WithAlpha( 0.16f ) );
				Paint.DrawRect( new Rect( 0, y, Width, RowHeight ) );
				Paint.SetBrushAndPen( TrackColor( tracks[index].Kind ) );
				Paint.DrawRect( new Rect( 0, y + 3, 3, RowHeight - 6 ), 1 );
			}
			else if ( index % 2 == 0 )
			{
				Paint.SetBrushAndPen( Color.White.WithAlpha( 0.018f ) );
				Paint.DrawRect( new Rect( 0, y, Width, RowHeight ) );
			}

			var contentRight = Width - TimelineEditorCanvas.ScrollbarGutter;
			Paint.SetDefaultFont( 8, selected ? 600 : 500 );
			Paint.SetPen( selected ? WeaponAnimatorTheme.Text : TrackColor( tracks[index].Kind ) );
			Paint.DrawText(
				new Rect( 8, y, contentRight - 65, RowHeight ),
				tracks[index].Target,
				TextFlag.LeftCenter | TextFlag.SingleLine );
			Paint.SetDefaultFont( 8, 500 );
			Paint.SetPen( WeaponAnimatorTheme.Muted );
			var countText = tracks[index].Keys.Count == 1
				? "1 key"
				: $"{tracks[index].Keys.Count} keys";
			Paint.DrawText(
				new Rect( contentRight - 50, y, 43, RowHeight ),
				countText,
				TextFlag.RightCenter | TextFlag.SingleLine );
		}

		if ( tracks.Count == 0 )
		{
			Paint.SetDefaultFont( 9, 500 );
			Paint.SetPen( WeaponAnimatorTheme.Muted );
			Paint.DrawText(
				LocalRect.Shrink( 12 ),
				"No keyed transform tracks",
				TextFlag.Center | TextFlag.WordWrap );
		}

		var gutter = new Rect(
			Width - TimelineEditorCanvas.ScrollbarGutter,
			0,
			TimelineEditorCanvas.ScrollbarGutter,
			Height );
		Paint.SetBrushAndPen( new Color( 0.055f, 0.061f, 0.068f ) );
		Paint.DrawRect( gutter );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton
			|| e.LocalPosition.x > Width - TimelineEditorCanvas.ScrollbarGutter
			|| _timeline.Clip is not { } clip )
		{
			base.OnMousePress( e );
			return;
		}

		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		var tracks = CurveEditingService.KeyedTracks( clip, view.Search );
		var index = (int)MathF.Floor(
			(e.LocalPosition.y + VerticalScrollbar.Value) / RowHeight );
		if ( index >= 0 && index < tracks.Count )
		{
			_timeline.Controller.SelectCurveTrack( clip, tracks[index].Id );
			e.Accepted = true;
			return;
		}
		base.OnMousePress( e );
	}

	protected override void OnScrollChanged()
	{
		base.OnScrollChanged();
		if ( _restoring || _timeline.Clip is not { } clip )
			return;
		_timeline.Controller.SetCurveTrackScroll( clip, VerticalScrollbar.Value );
		Update();
	}

	private void ConfigureScroll()
	{
		if ( _timeline.Clip is not { } clip )
			return;
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		var count = CurveEditingService.KeyedTracks( clip, view.Search ).Count;
		var maximum = Math.Max( 0, (int)MathF.Ceiling( count * RowHeight - Height ) );
		_restoring = true;
		VerticalScrollbar.Minimum = 0;
		VerticalScrollbar.Maximum = maximum;
		VerticalScrollbar.SingleStep = (int)RowHeight;
		VerticalScrollbar.PageStep = Math.Max( (int)Height, 1 );
		if ( _restoredClipId != clip.Id || _restoredSearch != view.Search )
		{
			_restoredClipId = clip.Id;
			_restoredSearch = view.Search;
			VerticalScrollbar.Value = Math.Clamp( (int)view.TrackScroll, 0, maximum );
		}
		_restoring = false;
	}

	private static Color TrackColor( RigControlKind kind ) => kind switch
	{
		RigControlKind.Weapon => WeaponAnimatorTheme.Amber,
		RigControlKind.Camera => WeaponAnimatorTheme.Green,
		_ => WeaponAnimatorTheme.Cyan
	};
}

internal sealed class CurveVerticalAxis : Widget
{
	private readonly TimelineEditorCanvas _timeline;
	public Func<(float Minimum, float Maximum)>? RangeProvider { get; set; }

	public CurveVerticalAxis(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		_timeline = timeline;
		SetStyles(
			"background-color: rgb(17,19,22); border: none;" +
			"border-right: 1px solid rgba(255,255,255,0.075);" );
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( new Color( 0.068f, 0.075f, 0.083f ) );
		Paint.DrawRect( LocalRect );
		var clip = _timeline.Clip;
		if ( clip is null || RangeProvider is null )
			return;
		var range = RangeProvider();
		var step = NiceStep( MathF.Max( range.Maximum - range.Minimum, 0.0001f ) / 5.0f );
		var first = MathF.Ceiling( range.Minimum / step ) * step;
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		for ( var value = first; value <= range.Maximum + step * 0.1f; value += step )
		{
			var y = Height - (value - range.Minimum)
				/ MathF.Max( range.Maximum - range.Minimum, 0.0001f ) * Height;
			Paint.SetPen( Color.White.WithAlpha( 0.08f ) );
			Paint.DrawLine( new Vector2( Width - 6, y ), new Vector2( Width, y ) );
			Paint.SetDefaultFont( 8, 500 );
			Paint.SetPen( WeaponAnimatorTheme.Muted );
			Paint.DrawText(
				new Rect( 2, y - 9, Width - 10, 18 ),
				FormatValue( value, view ),
				TextFlag.RightCenter | TextFlag.SingleLine );
		}
	}

	private static string FormatValue( float value, CurveViewState view )
	{
		if ( view.Mode == CurveEditorMode.Speed )
			return $"{value:0.##}×";
		var channels = view.VisibleChannels;
		if ( (channels & ~TransformCurveChannel.Rotation) == 0 )
			return $"{value:0.#}°";
		if ( (channels & ~TransformCurveChannel.Scale) == 0 )
			return $"{value:0.###}×";
		return $"{value:0.##}";
	}

	internal static float NiceStep( float raw )
	{
		if ( !WeaponAnimationMath.IsFinite( raw ) || raw <= 0 )
			return 1;
		var magnitude = MathF.Pow( 10, MathF.Floor( MathF.Log10( raw ) ) );
		var normalized = raw / magnitude;
		var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
		return nice * magnitude;
	}
}

internal sealed class CurveGraphWidget : Widget
{
	private const float PointHitRadius = 8;
	private const float HandleHitRadius = 7;
	private const float DragThreshold = 3;
	private readonly TimelineEditorCanvas _timeline;
	private CurveGraphDragMode _dragMode;
	private Vector2 _press;
	private Vector2 _drag;
	private bool _dragStarted;
	private bool _additive;
	private bool _toggle;
	private HashSet<Guid> _selectionAtPress = [];
	private Dictionary<Guid, CurveKeyStart> _keyStarts = [];
	private List<CurveSpeedStart> _speedStarts = [];
	private CurveHit? _activeHit;
	private (float Minimum, float Maximum) _panStartRange;
	private TransformCurveChannel _activeChannel = TransformCurveChannel.PositionX;
	private Guid _sampleClipId;
	private Guid _sampleTrackId;
	private TimelineFrameRange _sampleFrameRange;
	private TransformCurveChannel _sampleChannels;
	private int _sampleWidth;
	private Dictionary<TransformCurveChannel, IReadOnlyList<CurveGraphSample>>? _channelSamples;
	private (float Minimum, float Maximum)? _automaticRange;
	public Action? RangeChanged { get; set; }

	public CurveGraphWidget(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		_timeline = timeline;
		MouseTracking = true;
		FocusMode = FocusMode.Click;
		SetStyles( "background-color: rgb(12,14,16); border: none;" );
	}

	[Shortcut( "weaponanim.curves.delete", "DEL" )]
	private void ShortcutDelete() => _timeline.Controller.DeleteSelectedKeys();

	[Shortcut( "weaponanim.curves.backspace", "BACKSPACE" )]
	private void ShortcutBackspace() => _timeline.Controller.DeleteSelectedKeys();

	[Shortcut( "weaponanim.curves.fit", "F" )]
	private void ShortcutFit()
	{
		if ( _timeline.Clip is { } clip )
			_timeline.Controller.FitCurveVerticalRange( clip );
	}

	public void Refresh()
	{
		Update();
	}

	public void InvalidateData()
	{
		_channelSamples = null;
		_automaticRange = null;
	}

	public void InvalidateView()
	{
		// Horizontal range and channel changes are part of the sample cache key.
		_automaticRange = null;
	}

	protected override void OnResize()
	{
		base.OnResize();
		_channelSamples = null;
		_automaticRange = null;
		RangeChanged?.Invoke();
	}

	public (float Minimum, float Maximum) ResolveVerticalRange()
	{
		if ( _timeline.Clip is not { } clip )
			return (0, 1);
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		if ( view.HasVerticalRange
			&& view.VerticalMaximum - view.VerticalMinimum > 0.0001f )
			return (view.VerticalMinimum, view.VerticalMaximum);
		return _automaticRange ??= CalculateFitRange( clip, view );
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetBrushAndPen( WeaponAnimatorTheme.Background );
		Paint.DrawRect( LocalRect );
		if ( _timeline.Clip is not { } clip )
			return;
		var track = CurveEditingService.ResolveSelectedTrack(
			_timeline.Controller.Document,
			clip );
		if ( track is null )
		{
			DrawEmpty( "No keyed transform track selected" );
			return;
		}
		var verticalRange = ResolveVerticalRange();
		if ( track.Keys.Count < 2 )
		{
			DrawGrid( clip, verticalRange );
			DrawEmpty( "One key · no editable span" );
			DrawChannelPoints( clip, track, verticalRange );
			return;
		}

		DrawGrid( clip, verticalRange );
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		if ( view.Mode == CurveEditorMode.Speed )
			DrawSpeedCurves( clip, track, verticalRange );
		else
			DrawChannelCurves(
				clip,
				track,
				view.VisibleChannels,
				verticalRange );

		var playheadFrame = TimelineInteraction.TimeToFrame(
			_timeline.Controller.Document.Workspace.TimelineTime,
			clip.SampleRate );
		var playheadX = FrameToX( playheadFrame, clip );
		if ( playheadX >= 0 && playheadX <= Width )
		{
			Paint.SetPen( WeaponAnimatorTheme.Coral, 1.5f );
			Paint.DrawLine( new Vector2( playheadX, 0 ), new Vector2( playheadX, Height ) );
		}

		if ( _dragMode == CurveGraphDragMode.Marquee )
		{
			var rect = DragRect();
			Paint.SetBrushAndPen( WeaponAnimatorTheme.Cyan.WithAlpha( 0.12f ) );
			Paint.DrawRect( rect, 1 );
			Paint.ClearBrush();
			Paint.SetPen( WeaponAnimatorTheme.Cyan.WithAlpha( 0.9f ) );
			Paint.DrawRect( rect, 1 );
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( _timeline.Clip is not { } clip )
		{
			base.OnMousePress( e );
			return;
		}
		if ( e.MiddleMouseButton )
		{
			_dragMode = CurveGraphDragMode.Pan;
			_press = _drag = e.LocalPosition;
			_panStartRange = ResolveVerticalRange();
			e.Accepted = true;
			return;
		}
		if ( !e.LeftMouseButton )
		{
			base.OnMousePress( e );
			return;
		}

		Focus();
		var track = CurveEditingService.ResolveSelectedTrack(
			_timeline.Controller.Document,
			clip );
		if ( track is null )
			return;
		_press = _drag = e.LocalPosition;
		_dragStarted = false;
		_additive = e.HasShift;
		_toggle = e.HasCtrl;
		_selectionAtPress = _timeline.Controller.SelectedKeys.ToHashSet();
		_activeHit = HitTest( clip, track, e.LocalPosition );
		if ( _activeHit is { Kind: CurveHitKind.Handle } )
		{
			_dragMode = CurveGraphDragMode.Handle;
			_timeline.Controller.BeginContinuousEdit( "Edit curve handle" );
			e.Accepted = true;
			return;
		}
		if ( _activeHit is { } hit )
		{
			_activeChannel = hit.Channel;
			ApplyPointSelection( hit.KeyId );
			_dragMode = CurveGraphDragMode.Points;
			var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
			if ( view.Mode == CurveEditorMode.Speed )
				_speedStarts = SelectedSpeedStarts( track );
			else
				_keyStarts = SelectedKeyStarts( track );
			_timeline.Controller.BeginContinuousEdit( "Move curve points" );
			e.Accepted = true;
			return;
		}

		_dragMode = CurveGraphDragMode.Marquee;
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _dragMode == CurveGraphDragMode.None )
		{
			base.OnMouseMove( e );
			return;
		}
		_drag = e.LocalPosition;
		_dragStarted |= (_drag - _press).Length >= DragThreshold;
		switch ( _dragMode )
		{
			case CurveGraphDragMode.Pan:
				PanVertical();
				break;
			case CurveGraphDragMode.Marquee:
				ApplyMarquee();
				break;
			case CurveGraphDragMode.Points when _dragStarted:
				MoveSelectedPoints();
				break;
			case CurveGraphDragMode.Handle:
				MoveHandle( e.HasAlt );
				break;
		}
		Update();
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( _dragMode == CurveGraphDragMode.None )
		{
			base.OnMouseReleased( e );
			return;
		}
		if ( e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle )
		{
			base.OnMouseReleased( e );
			return;
		}

		if ( _dragMode == CurveGraphDragMode.Marquee )
			ApplyMarquee();
		else if ( _dragMode is CurveGraphDragMode.Points or CurveGraphDragMode.Handle )
		{
			if ( _timeline.Clip is { } clip
				&& CurveEditingService.ResolveSelectedTrack(
					_timeline.Controller.Document,
					clip ) is { } track )
			{
				_timeline.Controller.UpdateContinuousEdit( _ =>
					CurveEditingService.RepairTrack( track ) );
			}
			if ( _dragMode == CurveGraphDragMode.Points )
				_timeline.Controller.EndCurvePointMove();
			else
				_timeline.Controller.EndContinuousEdit();
		}
		else if ( _dragMode == CurveGraphDragMode.Pan )
		{
			_timeline.Controller.MarkWorkspacePreferenceChanged( "Pan curve values" );
		}

		_dragMode = CurveGraphDragMode.None;
		_activeHit = null;
		_keyStarts.Clear();
		_speedStarts.Clear();
		Update();
		e.Accepted = true;
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		if ( e.HasCtrl )
		{
			_timeline.Zoom( e.Delta > 0 );
			e.Accept();
			return;
		}
		if ( _timeline.Clip is not { } clip )
		{
			base.OnMouseWheel( e );
			return;
		}
		var range = ResolveVerticalRange();
		var cursor = ValueFromY( e.Position.y, range );
		var factor = e.Delta > 0 ? 0.82f : 1.22f;
		var minimum = cursor + (range.Minimum - cursor) * factor;
		var maximum = cursor + (range.Maximum - cursor) * factor;
		if ( maximum - minimum >= 0.0001f )
			_timeline.Controller.SetCurveVerticalRange( clip, minimum, maximum );
		e.Accept();
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var menu = new ContextMenu( this );
		menu.AddOption(
			"Align selected handles",
			"link",
			_timeline.Controller.AlignSelectedCurveHandles ).Enabled =
			_timeline.Controller.SelectedKeys.Count > 0;
		menu.AddOption(
			"Break selected handles",
			"link_off",
			() => _timeline.Controller.SetSelectedCurveHandleMode(
				CurveHandleMode.Free ) ).Enabled =
			_timeline.Controller.SelectedKeys.Count > 0;
		menu.AddSeparator();
		menu.AddOption( "Linear", null, () =>
			_timeline.Controller.ApplyCurvePreset( CurvePreset.Linear ) );
		menu.AddOption( "Ease In", null, () =>
			_timeline.Controller.ApplyCurvePreset( CurvePreset.EaseIn ) );
		menu.AddOption( "Ease Out", null, () =>
			_timeline.Controller.ApplyCurvePreset( CurvePreset.EaseOut ) );
		menu.AddOption( "Ease In-Out", null, () =>
			_timeline.Controller.ApplyCurvePreset( CurvePreset.EaseInOut ) );
		menu.OpenAtCursor( false );
		e.Accepted = true;
	}

	private void DrawGrid(
		WeaponAnimationClip clip,
		(float Minimum, float Maximum) range )
	{
		var yStep = CurveVerticalAxis.NiceStep(
			MathF.Max( range.Maximum - range.Minimum, 0.0001f ) / 5.0f );
		for ( var value = MathF.Ceiling( range.Minimum / yStep ) * yStep;
			value <= range.Maximum + yStep * 0.1f;
			value += yStep )
		{
			var y = ValueToY( value, range );
			Paint.SetPen( Color.White.WithAlpha( MathF.Abs( value ) < yStep * 0.1f ? 0.13f : 0.055f ) );
			Paint.DrawLine( new Vector2( 0, y ), new Vector2( Width, y ) );
		}

		var frameRange = _timeline.VisibleRange;
		var spacing = TimelineInteraction.TickSpacing(
			Width / MathF.Max( frameRange.Span, 1 ) );
		var first = (int)MathF.Ceiling(
			frameRange.StartFrame / (float)spacing.MinorFrames ) * spacing.MinorFrames;
		for ( var frame = first;
			frame <= frameRange.EndFrame;
			frame += spacing.MinorFrames )
		{
			var major = frame % spacing.MajorFrames == 0;
			Paint.SetPen( Color.White.WithAlpha( major ? 0.095f : 0.032f ) );
			var x = FrameToX( frame, clip );
			Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Height ) );
		}
	}

	private void DrawSpeedCurves(
		WeaponAnimationClip clip,
		TransformTrack track,
		(float Minimum, float Maximum) range )
	{
		var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
		for ( var index = 0; index < ordered.Length - 1; index++ )
		{
			var start = ordered[index];
			var end = ordered[index + 1];
			var span = track.FindCurveSpan( start.Id, end.Id );
			var points = new List<Vector2>( 34 );
			for ( var sample = 0; sample <= 32; sample++ )
			{
				var u = sample / 32.0f;
				var rate = EffectiveSpeed( span, track.Interpolation, u );
				points.Add( new Vector2(
					FrameToX(
						(start.Time + (end.Time - start.Time) * u) * clip.SampleRate,
						clip ),
					ValueToY( rate, range ) ) );
			}
			Paint.SetPen( WeaponAnimatorTheme.Cyan, 2 );
			Paint.DrawLine( points.ToArray() );
		}

		foreach ( var point in SpeedPoints( clip, track ) )
			DrawPoint( point, WeaponAnimatorTheme.Cyan, range );
		DrawSelectedHandles( clip, track );
	}

	private void DrawChannelCurves(
		WeaponAnimationClip clip,
		TransformTrack track,
		TransformCurveChannel channels,
		(float Minimum, float Maximum) range )
	{
		var samplesByChannel = ResolveChannelSamples( clip, track, channels );
		foreach ( var channel in CurveEditingService.Channels.Where( x =>
			(channels & x) != 0 ) )
		{
			if ( !samplesByChannel.TryGetValue( channel, out var samples ) )
				continue;
			Paint.SetPen( ChannelColor( channel ), 1.75f );
			Paint.DrawLine( samples.Select( x => new Vector2(
				FrameToX( x.Frame, clip ),
				ValueToY( x.Value, range ) ) ).ToArray() );
		}
		DrawChannelPoints( clip, track, range );
		DrawSelectedHandles( clip, track );
	}

	private void DrawChannelPoints(
		WeaponAnimationClip clip,
		TransformTrack track,
		(float Minimum, float Maximum) range )
	{
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		foreach ( var channel in CurveEditingService.Channels.Where( x =>
			(view.VisibleChannels & x) != 0 ) )
		{
			foreach ( var sample in CurveEditingService.ChannelSamples( track, channel ) )
			{
				DrawPoint( new CurvePoint(
					sample.Key.Id,
					channel,
					sample.Key.Time * clip.SampleRate,
					sample.Value,
					null,
					false ), ChannelColor( channel ), range );
			}
		}
	}

	private void DrawPoint(
		CurvePoint point,
		Color color,
		(float Minimum, float Maximum) range )
	{
		var clip = _timeline.Clip;
		if ( clip is null )
			return;
		var center = new Vector2(
			MathF.Round( FrameToX( point.Frame, clip ) ),
			MathF.Round( ValueToY( point.Value, range ) ) );
		if ( center.x < -8 || center.x > Width + 8 )
			return;
		var selected = _timeline.Controller.SelectedKeys.Contains( point.KeyId );
		var size = selected ? 10.0f : 8.0f;
		var rect = new Rect(
			center.x - size * 0.5f,
			center.y - size * 0.5f,
			size,
			size );
		Paint.ClearPen();
		Paint.SetBrush( selected ? Color.White : color );
		Paint.DrawRect( rect );
		Paint.ClearBrush();
		Paint.SetPen( color.WithAlpha( 0.95f ), 1 );
		Paint.DrawRect( rect );
	}

	private void DrawSelectedHandles( WeaponAnimationClip clip, TransformTrack track )
	{
		foreach ( var handle in HandlePoints( clip, track ).Where( x =>
			_timeline.Controller.SelectedKeys.Contains( x.KeyId ) ) )
		{
			Paint.SetPen( handle.Color.WithAlpha( 0.78f ), 1 );
			Paint.DrawLine( handle.Origin, handle.Position );
			Paint.SetBrushAndPen( handle.Color );
			Paint.DrawRect(
				new Rect(
					handle.Position.x - 4,
					handle.Position.y - 4,
					8,
					8 ),
				1 );
		}
	}

	private CurveHit? HitTest(
		WeaponAnimationClip clip,
		TransformTrack track,
		Vector2 position )
	{
		var handle = HandlePoints( clip, track )
			.Where( x => _timeline.Controller.SelectedKeys.Contains( x.KeyId ) )
			.OrderBy( x => (x.Position - position).Length )
			.FirstOrDefault();
		if ( handle.KeyId != Guid.Empty
			&& (handle.Position - position).Length <= HandleHitRadius )
		{
			return new CurveHit(
				CurveHitKind.Handle,
				handle.KeyId,
				handle.Channel,
				handle.SpanId,
				handle.Incoming,
				handle.StartKeyId,
				handle.EndKeyId );
		}

		var range = ResolveVerticalRange();
		var points = CurrentPoints( clip, track )
			.Select( point => (Point: point, Position: new Vector2(
				FrameToX( point.Frame, clip ),
				ValueToY( point.Value, range ) )) )
			.OrderBy( x => (x.Position - position).Length );
		foreach ( var item in points )
		{
			if ( (item.Position - position).Length <= PointHitRadius )
			{
				return new CurveHit(
					CurveHitKind.Point,
					item.Point.KeyId,
					item.Point.Channel,
					item.Point.Span?.Id ?? Guid.Empty,
					item.Point.IsSpanEnd,
					item.Point.Span?.StartKeyId ?? Guid.Empty,
					item.Point.Span?.EndKeyId ?? Guid.Empty );
			}
		}
		return null;
	}

	private void ApplyPointSelection( Guid keyId )
	{
		if ( _toggle )
			_timeline.Controller.ToggleSelectedKeys( [keyId] );
		else if ( _additive )
			_timeline.Controller.SelectKeys( [keyId], true );
		else if ( !_timeline.Controller.SelectedKeys.Contains( keyId ) )
			_timeline.Controller.SetSelectedKeys( [keyId] );
	}

	private void ApplyMarquee()
	{
		if ( _timeline.Clip is not { } clip
			|| CurveEditingService.ResolveSelectedTrack(
				_timeline.Controller.Document,
				clip ) is not { } track )
			return;
		var rect = DragRect();
		var range = ResolveVerticalRange();
		var hits = CurrentPoints( clip, track )
			.Where( point => Contains( rect, new Vector2(
				FrameToX( point.Frame, clip ),
				ValueToY( point.Value, range ) ) ) )
			.Select( x => x.KeyId )
			.ToHashSet();
		var result = TimelineInteraction.CombineKeySelection(
			_selectionAtPress,
			hits,
			_additive,
			_toggle );
		_timeline.Controller.SetSelectedKeys( result );
	}

	private void MoveSelectedPoints()
	{
		if ( _timeline.Clip is not { } clip
			|| CurveEditingService.ResolveSelectedTrack(
				_timeline.Controller.Document,
				clip ) is not { } track
			|| _keyStarts.Count == 0 )
			return;
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		if ( view.Mode == CurveEditorMode.Speed )
		{
			MoveSelectedSpeedPoints( clip, track );
			return;
		}
		var requested = XToFrame( _drag.x, clip ) - XToFrame( _press.x, clip );
		var startFrames = _keyStarts.Values.Select( x =>
			TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) );
		var deltaFrames = TimelineInteraction.ClampGroupFrameDelta(
			startFrames,
			requested,
			TimelineInteraction.LastFrame( clip ) );
		var valueDelta = ValueFromY( _drag.y, ResolveVerticalRange() )
			- ValueFromY( _press.y, ResolveVerticalRange() );
		var trackId = track.Id;
		var channel = _activeChannel;
		var starts = _keyStarts.ToDictionary( x => x.Key, x => x.Value );
		_timeline.Controller.UpdateContinuousEdit( document =>
		{
			var editedClip = document.Clips.FirstOrDefault( x => x.Id == clip.Id );
			var editedTrack = editedClip?.Tracks.FirstOrDefault( x => x.Id == trackId );
			if ( editedTrack is null )
				return;
			foreach ( var key in editedTrack.Keys.Where( x => starts.ContainsKey( x.Id ) ) )
			{
				var start = starts[key.Id];
				key.Time = TimelineInteraction.FrameToTime(
					TimelineInteraction.TimeToFrame( start.Time, clip.SampleRate )
						+ deltaFrames,
					clip.SampleRate );
				CurveEditingService.SetKeyValue(
					key,
					channel,
					start.Value + valueDelta );
			}
			editedTrack.Keys = editedTrack.Keys.OrderBy( x => x.Time ).ToList();
			IdleBindPoseService.MarkAuthored( editedClip );
		} );
	}

	private void MoveSelectedSpeedPoints(
		WeaponAnimationClip clip,
		TransformTrack track )
	{
		if ( _speedStarts.Count == 0 )
			return;
		var valueDelta = ValueFromY( _drag.y, ResolveVerticalRange() )
			- ValueFromY( _press.y, ResolveVerticalRange() );
		var trackId = track.Id;
		var starts = _speedStarts.ToArray();
		_timeline.Controller.UpdateContinuousEdit( document =>
		{
			var editedClip = document.Clips.FirstOrDefault( x => x.Id == clip.Id );
			var editedTrack = editedClip?.Tracks.FirstOrDefault( x => x.Id == trackId );
			if ( editedClip is null || editedTrack is null )
				return;
			foreach ( var start in starts )
			{
				var span = editedTrack.EnsureCurveSpan(
					start.StartKeyId,
					start.EndKeyId );
				if ( !span.HasSpeedCurve )
				InitializeSpeedCurve(
					span,
					start.Interpolation );
				span.HasSpeedCurve = true;
				if ( span.CustomChannels == TransformCurveChannel.None
					&& !span.HasInterpolationOverride )
				{
					span.HasInterpolationOverride = true;
					span.Interpolation = TrackInterpolation.Linear;
				}
				if ( start.MoveStart )
					span.Speed.StartRate = MathF.Max( start.StartRate + valueDelta, 0 );
				if ( start.MoveEnd )
					span.Speed.EndRate = MathF.Max( start.EndRate + valueDelta, 0 );
			}
			IdleBindPoseService.MarkAuthored( editedClip );
		} );
	}

	private void MoveHandle( bool breakHandles )
	{
		if ( _activeHit is not { Kind: CurveHitKind.Handle } hit
			|| _timeline.Clip is not { } clip
			|| CurveEditingService.ResolveSelectedTrack(
				_timeline.Controller.Document,
				clip ) is not { } track )
			return;
		var trackId = track.Id;
		var range = ResolveVerticalRange();
		_timeline.Controller.UpdateContinuousEdit( document =>
		{
			var editedTrack = document.Clips.FirstOrDefault( x => x.Id == clip.Id )?
				.Tracks.FirstOrDefault( x => x.Id == trackId );
			if ( editedTrack is null )
				return;
			if ( document.Workspace.EnsureCurveView( clip.Id ).Mode == CurveEditorMode.Speed )
			{
				var span = hit.SpanId != Guid.Empty
					? editedTrack.CurveSpans.FirstOrDefault( x => x.Id == hit.SpanId )
					: editedTrack.EnsureCurveSpan( hit.StartKeyId, hit.EndKeyId );
				if ( span is null )
					return;
				if ( !span.HasSpeedCurve )
					InitializeSpeedCurve(
						span,
						span.HasInterpolationOverride
							? span.Interpolation
							: editedTrack.Interpolation );
				span.HasSpeedCurve = true;
				if ( span.CustomChannels == TransformCurveChannel.None
					&& !span.HasInterpolationOverride )
				{
					span.HasInterpolationOverride = true;
					span.Interpolation = TrackInterpolation.Linear;
				}
				var originRate = hit.Incoming ? span.Speed.EndRate : span.Speed.StartRate;
				var originFrame = hit.Incoming
					? editedTrack.Keys.First( x => x.Id == span.EndKeyId ).Time * clip.SampleRate
					: editedTrack.Keys.First( x => x.Id == span.StartKeyId ).Time * clip.SampleRate;
				var handleFrame = XToFrameFloat( _drag.x, clip );
				var handleRate = MathF.Max( ValueFromY( _drag.y, range ), 0 );
				var deltaU = MathF.Max(
					MathF.Abs( handleFrame - originFrame )
						/ MathF.Max(
							MathF.Abs(
								editedTrack.Keys.First( x => x.Id == span.EndKeyId ).Time
									- editedTrack.Keys.First( x => x.Id == span.StartKeyId ).Time )
								* clip.SampleRate,
							0.0001f ),
					0.0001f );
				var slope = (handleRate - originRate) / (hit.Incoming ? -deltaU : deltaU);
				if ( hit.Incoming )
					span.Speed.EndSlope = slope;
				else
					span.Speed.StartSlope = slope;
				if ( breakHandles )
				{
					if ( hit.Incoming )
						span.Speed.EndHandleMode = CurveHandleMode.Free;
					else
						span.Speed.StartHandleMode = CurveHandleMode.Free;
				}
				else if ( hit.Incoming )
				{
					span.Speed.EndHandleMode = CurveHandleMode.Aligned;
				}
				else
				{
					span.Speed.StartHandleMode = CurveHandleMode.Aligned;
				}
			}
			else
			{
				var key = editedTrack.Keys.FirstOrDefault( x => x.Id == hit.KeyId );
				if ( key is null )
					return;
				var originValue = CurveEditingService.ChannelSamples( editedTrack, hit.Channel )
					.First( x => x.Key.Id == key.Id ).Value;
				var originFrame = key.Time * clip.SampleRate;
				var deltaSeconds = (XToFrameFloat( _drag.x, clip ) - originFrame)
					/ MathF.Max( clip.SampleRate, 1 );
				if ( MathF.Abs( deltaSeconds ) < 0.0001f )
					return;
				var value = ValueFromY( _drag.y, range );
				var tangent = (value - originValue) / deltaSeconds;
				CurveEditingService.SetTangent(
					key,
					hit.Channel,
					hit.Incoming,
					tangent,
					breakHandles );
				var span = hit.SpanId != Guid.Empty
					? editedTrack.CurveSpans.FirstOrDefault( x => x.Id == hit.SpanId )
					: editedTrack.EnsureCurveSpan( hit.StartKeyId, hit.EndKeyId );
				if ( span is null )
					return;
				span.CustomChannels |= hit.Channel;
			}
			IdleBindPoseService.MarkAuthored(
				document.Clips.First( x => x.Id == clip.Id ) );
		} );
	}

	private void PanVertical()
	{
		if ( _timeline.Clip is not { } clip )
			return;
		var span = _panStartRange.Maximum - _panStartRange.Minimum;
		var delta = (_drag.y - _press.y) / MathF.Max( Height, 1 ) * span;
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		view.HasVerticalRange = true;
		view.VerticalMinimum = _panStartRange.Minimum + delta;
		view.VerticalMaximum = _panStartRange.Maximum + delta;
		RangeChanged?.Invoke();
	}

	private IReadOnlyList<CurvePoint> CurrentPoints(
		WeaponAnimationClip clip,
		TransformTrack track )
	{
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		if ( view.Mode == CurveEditorMode.Speed )
			return SpeedPoints( clip, track );
		return CurveEditingService.Channels
			.Where( x => (view.VisibleChannels & x) != 0 )
			.SelectMany( channel => CurveEditingService.ChannelSamples( track, channel )
				.Select( sample => new CurvePoint(
					sample.Key.Id,
					channel,
					sample.Key.Time * clip.SampleRate,
					sample.Value,
					null,
					false ) ) )
			.ToArray();
	}

	private IReadOnlyList<CurvePoint> SpeedPoints(
		WeaponAnimationClip clip,
		TransformTrack track )
	{
		var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
		var result = new List<CurvePoint>();
		for ( var index = 0; index < ordered.Length - 1; index++ )
		{
			var start = ordered[index];
			var end = ordered[index + 1];
			var span = track.FindCurveSpan( start.Id, end.Id );
			result.Add( new CurvePoint(
				start.Id,
				TransformCurveChannel.None,
				start.Time * clip.SampleRate,
				EffectiveSpeed( span, track.Interpolation, 0 ),
				span,
				false ) );
			result.Add( new CurvePoint(
				end.Id,
				TransformCurveChannel.None,
				end.Time * clip.SampleRate,
				EffectiveSpeed( span, track.Interpolation, 1 ),
				span,
				true ) );
		}
		return result;
	}

	private IReadOnlyList<CurveHandlePoint> HandlePoints(
		WeaponAnimationClip clip,
		TransformTrack track )
	{
		var view = _timeline.Controller.Document.Workspace.EnsureCurveView( clip.Id );
		var range = ResolveVerticalRange();
		var result = new List<CurveHandlePoint>();
		if ( view.Mode == CurveEditorMode.Speed )
		{
			var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
			for ( var index = 0; index < ordered.Length - 1; index++ )
			{
				var start = ordered[index];
				var end = ordered[index + 1];
				var span = track.FindCurveSpan( start.Id, end.Id );
				var frameA = start.Time * clip.SampleRate;
				var frameB = end.Time * clip.SampleRate;
				var frameDelta = frameB - frameA;
				var interpolation = span?.HasInterpolationOverride == true
					? span.Interpolation
					: track.Interpolation;
				var startRate = EffectiveSpeed( span, track.Interpolation, 0 );
				var endRate = EffectiveSpeed( span, track.Interpolation, 1 );
				var startSlope = span?.HasSpeedCurve == true
					? span.Speed.StartSlope
					: interpolation == TrackInterpolation.Cubic ? 6 : 0;
				var endSlope = span?.HasSpeedCurve == true
					? span.Speed.EndSlope
					: interpolation == TrackInterpolation.Cubic ? -6 : 0;
				AddHandle(
					result,
					start.Id,
					TransformCurveChannel.None,
					span?.Id ?? Guid.Empty,
					start.Id,
					end.Id,
					false,
					frameA,
					startRate,
					frameA + frameDelta / 3,
					startRate + startSlope / 3,
					WeaponAnimatorTheme.Cyan,
					clip,
					range );
				AddHandle(
					result,
					end.Id,
					TransformCurveChannel.None,
					span?.Id ?? Guid.Empty,
					start.Id,
					end.Id,
					true,
					frameB,
					endRate,
					frameB - frameDelta / 3,
					endRate - endSlope / 3,
					WeaponAnimatorTheme.Cyan,
					clip,
					range );
			}
			return result;
		}

		var samplesByChannel = CurveEditingService.Channels
			.Where( x => (view.VisibleChannels & x) != 0 )
			.ToDictionary( x => x, x => CurveEditingService.ChannelSamples( track, x ) );
		var orderedKeys = track.Keys.OrderBy( x => x.Time ).ToArray();
		for ( var keyIndex = 0; keyIndex < orderedKeys.Length - 1; keyIndex++ )
		{
			var start = orderedKeys[keyIndex];
			var end = orderedKeys[keyIndex + 1];
			var span = track.FindCurveSpan( start.Id, end.Id );
			var duration = MathF.Max( end.Time - start.Time, 0.0001f );
			foreach ( var channel in samplesByChannel.Keys )
			{
				var startValue = samplesByChannel[channel].First( x => x.Key.Id == start.Id ).Value;
				var endValue = samplesByChannel[channel].First( x => x.Key.Id == end.Id ).Value;
				var interpolation = span?.HasInterpolationOverride == true
					? span.Interpolation
					: track.Interpolation;
				var secant = (endValue - startValue) / duration;
				var custom = span is not null
					&& (span.CustomChannels & channel) != 0;
				var outgoing = custom
					? CurveEditingService.GetTangent( start, channel, false )
					: interpolation == TrackInterpolation.Linear ? secant : 0;
				var incoming = custom
					? CurveEditingService.GetTangent( end, channel, true )
					: interpolation == TrackInterpolation.Linear ? secant : 0;
				AddHandle(
					result,
					start.Id,
					channel,
					span?.Id ?? Guid.Empty,
					start.Id,
					end.Id,
					false,
					start.Time * clip.SampleRate,
					startValue,
					(start.Time + duration / 3) * clip.SampleRate,
					startValue + outgoing * duration / 3,
					ChannelColor( channel ),
					clip,
					range );
				AddHandle(
					result,
					end.Id,
					channel,
					span?.Id ?? Guid.Empty,
					start.Id,
					end.Id,
					true,
					end.Time * clip.SampleRate,
					endValue,
					(end.Time - duration / 3) * clip.SampleRate,
					endValue - incoming * duration / 3,
					ChannelColor( channel ),
					clip,
					range );
			}
		}
		return result;
	}

	private void AddHandle(
		List<CurveHandlePoint> result,
		Guid keyId,
		TransformCurveChannel channel,
		Guid spanId,
		Guid startKeyId,
		Guid endKeyId,
		bool incoming,
		float originFrame,
		float originValue,
		float handleFrame,
		float handleValue,
		Color color,
		WeaponAnimationClip clip,
		(float Minimum, float Maximum) range )
	{
		result.Add( new CurveHandlePoint(
			keyId,
			channel,
			spanId,
			startKeyId,
			endKeyId,
			incoming,
			new Vector2(
				FrameToX( originFrame, clip ),
				ValueToY( originValue, range ) ),
			new Vector2(
				FrameToX( handleFrame, clip ),
				ValueToY( handleValue, range ) ),
			color ) );
	}

	private IReadOnlyDictionary<TransformCurveChannel, IReadOnlyList<CurveGraphSample>>
		ResolveChannelSamples(
		WeaponAnimationClip clip,
		TransformTrack track,
		TransformCurveChannel channels )
	{
		var frameRange = _timeline.VisibleRange;
		var sampleWidth = Math.Max( (int)Width, 1 );
		if ( _channelSamples is not null
			&& _sampleClipId == clip.Id
			&& _sampleTrackId == track.Id
			&& _sampleFrameRange == frameRange
			&& _sampleChannels == channels
			&& _sampleWidth == sampleWidth )
			return _channelSamples;

		var activeChannels = CurveEditingService.Channels
			.Where( x => (channels & x) != 0 )
			.ToArray();
		var samples = activeChannels.ToDictionary(
			x => x,
			_ => new List<CurveGraphSample>() );
		var count = Math.Clamp( (int)Width / 3, 24, 512 );
		var previousRotations = new Dictionary<TransformCurveChannel, float>();
		var firstKey = track.Keys.OrderBy( x => x.Time ).First();
		var fallback = new Transform(
			firstKey.Position,
			firstKey.Rotation,
			firstKey.Scale );
		for ( var index = 0; index <= count; index++ )
		{
			var frame = frameRange.StartFrame + frameRange.Span * index / count;
			var transform = WeaponAnimationMath.SampleTrack(
				track,
				frame / MathF.Max( clip.SampleRate, 1 ),
				fallback );
			foreach ( var channel in activeChannels )
			{
				var value = TransformValue( transform, channel );
				if ( IsRotation( channel )
					&& previousRotations.TryGetValue( channel, out var previous ) )
				{
					value = CurveEditingService.UnwrapDegrees( previous, value );
				}
				previousRotations[channel] = value;
				samples[channel].Add( new CurveGraphSample( frame, value ) );
			}
		}

		_sampleClipId = clip.Id;
		_sampleTrackId = track.Id;
		_sampleFrameRange = frameRange;
		_sampleChannels = channels;
		_sampleWidth = sampleWidth;
		_channelSamples = samples.ToDictionary(
			x => x.Key,
			x => (IReadOnlyList<CurveGraphSample>)x.Value );
		return _channelSamples;
	}

	private (float Minimum, float Maximum) CalculateFitRange(
		WeaponAnimationClip clip,
		CurveViewState view )
	{
		var track = CurveEditingService.ResolveSelectedTrack(
			_timeline.Controller.Document,
			clip );
		if ( track is null )
			return (0, 1);
		var values = new List<float>();
		if ( view.Mode == CurveEditorMode.Speed )
		{
			var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
			for ( var spanIndex = 0; spanIndex < ordered.Length - 1; spanIndex++ )
			{
				var span = track.FindCurveSpan(
					ordered[spanIndex].Id,
					ordered[spanIndex + 1].Id );
				for ( var i = 0; i <= 24; i++ )
					values.Add( EffectiveSpeed(
						span,
						track.Interpolation,
						i / 24.0f ) );
			}
			if ( values.Count == 0 )
				values.AddRange( [0, 1, 2] );
			values.Add( 0 );
		}
		else
		{
			foreach ( var samples in ResolveChannelSamples(
				clip,
				track,
				view.VisibleChannels ).Values )
			{
				values.AddRange( samples.Select( x => x.Value ) );
			}
		}

		if ( values.Count == 0 )
			return (0, 1);
		var minimum = values.Min();
		var maximum = values.Max();
		var spanSize = MathF.Max( maximum - minimum, 0.1f );
		var padding = spanSize * 0.14f;
		return (minimum - padding, maximum + padding);
	}

	private Dictionary<Guid, CurveKeyStart> SelectedKeyStarts( TransformTrack track )
	{
		var samples = CurveEditingService.ChannelSamples( track, _activeChannel )
			.ToDictionary( x => x.Key.Id, x => x.Value );
		return track.Keys
			.Where( x => _timeline.Controller.SelectedKeys.Contains( x.Id ) )
			.ToDictionary(
				x => x.Id,
				x => new CurveKeyStart(
					x.Time,
					samples.GetValueOrDefault(
						x.Id,
						TransformValue(
							new Transform( x.Position, x.Rotation, x.Scale ),
							_activeChannel ) ) ) );
	}

	private List<CurveSpeedStart> SelectedSpeedStarts( TransformTrack track )
	{
		var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
		var result = new List<CurveSpeedStart>();
		for ( var index = 0; index < ordered.Length - 1; index++ )
		{
			var start = ordered[index];
			var end = ordered[index + 1];
			var moveStart = _timeline.Controller.SelectedKeys.Contains( start.Id );
			var moveEnd = _timeline.Controller.SelectedKeys.Contains( end.Id );
			if ( !moveStart && !moveEnd )
				continue;
			var span = track.FindCurveSpan( start.Id, end.Id );
			var interpolation = span?.HasInterpolationOverride == true
				? span.Interpolation
				: track.Interpolation;
			result.Add( new CurveSpeedStart(
				start.Id,
				end.Id,
				EffectiveSpeed( span, track.Interpolation, 0 ),
				EffectiveSpeed( span, track.Interpolation, 1 ),
				interpolation,
				moveStart,
				moveEnd ) );
		}
		return result;
	}

	private static void InitializeSpeedCurve(
		TransformCurveSpan span,
		TrackInterpolation interpolation )
	{
		span.Speed = interpolation switch
		{
			TrackInterpolation.Stepped => new MotionRateCurve
			{
				StartRate = 0,
				EndRate = 0
			},
			TrackInterpolation.Cubic => new MotionRateCurve
			{
				StartRate = 0,
				EndRate = 0,
				StartSlope = 6,
				EndSlope = -6
			},
			_ => new MotionRateCurve()
		};
	}

	private Rect DragRect() => new(
		MathF.Min( _press.x, _drag.x ),
		MathF.Min( _press.y, _drag.y ),
		MathF.Abs( _drag.x - _press.x ),
		MathF.Abs( _drag.y - _press.y ) );

	private float FrameToX( float frame, WeaponAnimationClip clip )
	{
		var range = _timeline.VisibleRange;
		return (frame - range.StartFrame) / MathF.Max( range.Span, 1 ) * Width;
	}

	private int XToFrame( float x, WeaponAnimationClip clip ) =>
		Math.Clamp(
			(int)MathF.Round( XToFrameFloat( x, clip ) ),
			0,
			TimelineInteraction.LastFrame( clip ) );

	private float XToFrameFloat( float x, WeaponAnimationClip clip )
	{
		var range = _timeline.VisibleRange;
		return range.StartFrame + x / MathF.Max( Width, 1 ) * range.Span;
	}

	private float ValueToY(
		float value,
		(float Minimum, float Maximum) range ) =>
		Height - (value - range.Minimum)
			/ MathF.Max( range.Maximum - range.Minimum, 0.0001f ) * Height;

	private float ValueFromY(
		float y,
		(float Minimum, float Maximum) range ) =>
		range.Minimum + (Height - y) / MathF.Max( Height, 1 )
			* (range.Maximum - range.Minimum);

	private static float EffectiveSpeed(
		TransformCurveSpan? span,
		TrackInterpolation fallback,
		float u )
	{
		if ( span?.HasSpeedCurve == true )
			return WeaponAnimationMath.SampleMotionRate( span.Speed, u );
		var interpolation = span?.HasInterpolationOverride == true
			? span.Interpolation
			: fallback;
		return interpolation switch
		{
			TrackInterpolation.Stepped => 0,
			TrackInterpolation.Cubic => MathF.Max( 6 * u * (1 - u), 0 ),
			_ => 1
		};
	}

	private void DrawEmpty( string text )
	{
		Paint.SetDefaultFont( 10, 500 );
		Paint.SetPen( WeaponAnimatorTheme.Muted );
		Paint.DrawText( LocalRect, text, TextFlag.Center );
	}

	private static float TransformValue(
		Transform transform,
		TransformCurveChannel channel )
	{
		var angles = transform.Rotation.Angles();
		return channel switch
		{
			TransformCurveChannel.PositionX => transform.Position.x,
			TransformCurveChannel.PositionY => transform.Position.y,
			TransformCurveChannel.PositionZ => transform.Position.z,
			TransformCurveChannel.RotationX => angles.pitch,
			TransformCurveChannel.RotationY => angles.yaw,
			TransformCurveChannel.RotationZ => angles.roll,
			TransformCurveChannel.ScaleX => transform.Scale.x,
			TransformCurveChannel.ScaleY => transform.Scale.y,
			_ => transform.Scale.z
		};
	}

	private static bool IsRotation( TransformCurveChannel channel ) =>
		(channel & TransformCurveChannel.Rotation) != 0;

	private static Color ChannelColor( TransformCurveChannel channel ) => channel switch
	{
		TransformCurveChannel.PositionX => new Color( 0.92f, 0.31f, 0.28f ),
		TransformCurveChannel.PositionY => new Color( 0.36f, 0.82f, 0.43f ),
		TransformCurveChannel.PositionZ => new Color( 0.30f, 0.58f, 0.96f ),
		TransformCurveChannel.RotationX => new Color( 1.00f, 0.50f, 0.33f ),
		TransformCurveChannel.RotationY => new Color( 0.55f, 0.93f, 0.54f ),
		TransformCurveChannel.RotationZ => new Color( 0.42f, 0.72f, 1.00f ),
		TransformCurveChannel.ScaleX => new Color( 0.95f, 0.56f, 0.72f ),
		TransformCurveChannel.ScaleY => new Color( 0.65f, 0.90f, 0.71f ),
		_ => new Color( 0.62f, 0.77f, 0.98f )
	};

	private static bool Contains( Rect rect, Vector2 point ) =>
		point.x >= rect.Left
		&& point.x <= rect.Right
		&& point.y >= rect.Top
		&& point.y <= rect.Bottom;

	private readonly record struct CurveGraphSample( float Frame, float Value );
	private readonly record struct CurveKeyStart( float Time, float Value );
	private readonly record struct CurveSpeedStart(
		Guid StartKeyId,
		Guid EndKeyId,
		float StartRate,
		float EndRate,
		TrackInterpolation Interpolation,
		bool MoveStart,
		bool MoveEnd );
	private readonly record struct CurvePoint(
		Guid KeyId,
		TransformCurveChannel Channel,
		float Frame,
		float Value,
		TransformCurveSpan? Span,
		bool IsSpanEnd );
	private readonly record struct CurveHandlePoint(
		Guid KeyId,
		TransformCurveChannel Channel,
		Guid SpanId,
		Guid StartKeyId,
		Guid EndKeyId,
		bool Incoming,
		Vector2 Origin,
		Vector2 Position,
		Color Color );
	private readonly record struct CurveHit(
		CurveHitKind Kind,
		Guid KeyId,
		TransformCurveChannel Channel,
		Guid SpanId,
		bool Incoming,
		Guid StartKeyId,
		Guid EndKeyId );

	private enum CurveHitKind
	{
		Point,
		Handle
	}

	private enum CurveGraphDragMode
	{
		None,
		Marquee,
		Points,
		Handle,
		Pan
	}
}
