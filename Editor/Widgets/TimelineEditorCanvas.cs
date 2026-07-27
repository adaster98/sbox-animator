#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

internal sealed class TimelineEditorCanvas : Widget
{
	internal const float TrackHeaderWidth = 170;
	internal const float GraphStartX = TrackHeaderWidth + 10;
	internal const float ScrollbarGutter = WeaponAnimatorTheme.ScrollbarGutter;
	internal const float TrackHeight = 22;
	private readonly WeaponAnimatorController _controller;
	private readonly TimelineRangeNavigator _navigator;
	private readonly TimelineFrameRuler _ruler;
	private readonly TimelineTrackArea _tracks;

	public TimelineEditorCanvas(
		WeaponAnimatorController controller,
		Widget? parent = null ) : base( parent )
	{
		_controller = controller;
		MinimumSize = new Vector2( 320, 120 );
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;
		SetStyles( "background-color: rgb(12,14,16); border: none;" );

		_navigator = new TimelineRangeNavigator( this, this ) { FixedHeight = 22 };
		_ruler = new TimelineFrameRuler( this, this ) { FixedHeight = 24 };
		_tracks = new TimelineTrackArea( this, this );
		Layout.Add( _navigator );
		Layout.Add( _ruler );
		Layout.Add( _tracks, 1 );

		_controller.DocumentChanged += Refresh;
		_controller.PoseChanged += Refresh;
		_controller.SelectionChanged += Refresh;
		_controller.TimelineChanged += Refresh;
		_controller.TimelineViewChanged += Refresh;
		_controller.PlaybackChanged += Refresh;
	}

	public override void OnDestroyed()
	{
		_controller.DocumentChanged -= Refresh;
		_controller.PoseChanged -= Refresh;
		_controller.SelectionChanged -= Refresh;
		_controller.TimelineChanged -= Refresh;
		_controller.TimelineViewChanged -= Refresh;
		_controller.PlaybackChanged -= Refresh;
		base.OnDestroyed();
	}

	internal WeaponAnimatorController Controller => _controller;
	internal WeaponAnimationClip? Clip => _controller.Document.GetSelectedClip();

	internal TimelineFrameRange VisibleRange =>
		Clip is null
			? default
			: _controller.GetTimelineRange( Clip );

	internal float BodyRight( float width ) =>
		MathF.Max( GraphStartX + 1, width - ScrollbarGutter );

	internal float FrameToX( float frame, float width )
	{
		var range = VisibleRange;
		var bodyWidth = MathF.Max( BodyRight( width ) - GraphStartX, 1 );
		return GraphStartX
			+ (frame - range.StartFrame) / MathF.Max( range.Span, 1) * bodyWidth;
	}

	internal int XToFrame( float x, float width )
	{
		var clip = Clip;
		if ( clip is null )
			return 0;
		var range = VisibleRange;
		var amount = (x - GraphStartX)
			/ MathF.Max( BodyRight( width ) - GraphStartX, 1 );
		return Math.Clamp(
			(int)MathF.Round( range.StartFrame + amount * range.Span ),
			0,
			TimelineInteraction.LastFrame( clip ) );
	}

	internal float FullFrameToX( float frame, float width )
	{
		var clip = Clip;
		if ( clip is null )
			return GraphStartX;
		return GraphStartX
			+ frame / TimelineInteraction.LastFrame( clip )
			* MathF.Max( BodyRight( width ) - GraphStartX, 1 );
	}

	internal int FullXToFrame( float x, float width )
	{
		var clip = Clip;
		if ( clip is null )
			return 0;
		var amount = (x - GraphStartX)
			/ MathF.Max( BodyRight( width ) - GraphStartX, 1 );
		return Math.Clamp(
			(int)MathF.Round( amount * TimelineInteraction.LastFrame( clip ) ),
			0,
			TimelineInteraction.LastFrame( clip ) );
	}

	internal void Zoom( bool zoomIn )
	{
		var clip = Clip;
		if ( clip is null )
			return;
		_controller.SetTimelineRange(
			clip,
			TimelineInteraction.Zoom(
				VisibleRange,
				TimelineInteraction.LastFrame( clip ),
				zoomIn ) );
	}

	internal void DrawLabelGutter( float height )
	{
		Paint.SetBrushAndPen( new Color( 0.086f, 0.094f, 0.104f ) );
		Paint.DrawRect( new Rect( 0, 0, GraphStartX, height ) );
	}

	private void Refresh()
	{
		_navigator.Update();
		_ruler.Update();
		_tracks.Refresh();
		Update();
	}
}

internal sealed class TimelineRangeNavigator : Widget
{
	private const float HandleHitWidth = 8;
	private readonly TimelineEditorCanvas _timeline;
	private RangeDragMode _dragMode;
	private TimelineFrameRange _dragStartRange;
	private int _dragStartFrame;

	public TimelineRangeNavigator(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		_timeline = timeline;
		MouseTracking = true;
		Cursor = CursorShape.SizeH;
		ToolTip = "Drag the handles to zoom, or drag the cyan region to pan";
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( new Color( 0.06f, 0.07f, 0.08f ) );
		Paint.DrawRect( LocalRect );
		_timeline.DrawLabelGutter( Height );
		var clip = _timeline.Clip;
		if ( clip is null )
			return;

		var bodyRight = _timeline.BodyRight( Width );
		var rail = new Rect(
			TimelineEditorCanvas.GraphStartX,
			5,
			bodyRight - TimelineEditorCanvas.GraphStartX,
			12 );
		Paint.SetBrushAndPen( WeaponAnimatorTheme.SurfaceRaised );
		Paint.DrawRect( rail, 2 );

		var range = _timeline.VisibleRange;
		var left = _timeline.FullFrameToX( range.StartFrame, Width );
		var right = _timeline.FullFrameToX( range.EndFrame, Width );
		var active = new Rect( left, rail.Top, MathF.Max( right - left, 1 ), rail.Height );
		Paint.SetBrushAndPen( WeaponAnimatorTheme.Cyan.WithAlpha( 0.24f ) );
		Paint.DrawRect( active, 2 );
		Paint.SetBrushAndPen( WeaponAnimatorTheme.Cyan.WithAlpha( 0.92f ) );
		Paint.DrawRect( new Rect( left - 2, rail.Top - 1, 4, rail.Height + 2 ), 1 );
		Paint.DrawRect( new Rect( right - 2, rail.Top - 1, 4, rail.Height + 2 ), 1 );

		var playheadFrame = TimelineInteraction.TimeToFrame(
			_timeline.Controller.Document.Workspace.TimelineTime,
			clip.SampleRate );
		var playhead = _timeline.FullFrameToX( playheadFrame, Width );
		Paint.SetPen( WeaponAnimatorTheme.Coral.WithAlpha( 0.9f ) );
		Paint.DrawLine(
			new Vector2( playhead, rail.Top ),
			new Vector2( playhead, rail.Bottom ) );

		Paint.SetDefaultFont( 8, 550 );
		Paint.SetPen( WeaponAnimatorTheme.Muted );
		Paint.DrawText(
			new Rect( 8, 0, TimelineEditorCanvas.TrackHeaderWidth - 16, Height ),
			"VISIBLE RANGE",
			TextFlag.LeftCenter );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton || _timeline.Clip is null )
		{
			base.OnMousePress( e );
			return;
		}

		var range = _timeline.VisibleRange;
		var left = _timeline.FullFrameToX( range.StartFrame, Width );
		var right = _timeline.FullFrameToX( range.EndFrame, Width );
		_dragMode = MathF.Abs( e.LocalPosition.x - left ) <= HandleHitWidth
			? RangeDragMode.Start
			: MathF.Abs( e.LocalPosition.x - right ) <= HandleHitWidth
				? RangeDragMode.End
				: e.LocalPosition.x > left && e.LocalPosition.x < right
					? RangeDragMode.Body
					: RangeDragMode.None;
		if ( _dragMode == RangeDragMode.None )
			return;

		_dragStartRange = range;
		_dragStartFrame = _timeline.FullXToFrame( e.LocalPosition.x, Width );
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _dragMode == RangeDragMode.None
			|| (e.ButtonState & MouseButtons.Left) == 0
			|| _timeline.Clip is not { } clip )
		{
			base.OnMouseMove( e );
			return;
		}

		var frame = _timeline.FullXToFrame( e.LocalPosition.x, Width );
		var last = TimelineInteraction.LastFrame( clip );
		var range = _dragMode switch
		{
			RangeDragMode.Start => TimelineInteraction.ResizeStart(
				_dragStartRange,
				frame,
				last ),
			RangeDragMode.End => TimelineInteraction.ResizeEnd(
				_dragStartRange,
				frame,
				last ),
			_ => TimelineInteraction.Pan(
				_dragStartRange,
				frame - _dragStartFrame,
				last )
		};
		_timeline.Controller.SetTimelineRange( clip, range );
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( _dragMode == RangeDragMode.None || e.Button != MouseButtons.Left )
		{
			base.OnMouseReleased( e );
			return;
		}
		_dragMode = RangeDragMode.None;
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
		base.OnMouseWheel( e );
	}

	private enum RangeDragMode
	{
		None,
		Start,
		End,
		Body
	}
}

internal sealed class TimelineFrameRuler : Widget
{
	private readonly TimelineEditorCanvas _timeline;
	private bool _scrubbing;

	public TimelineFrameRuler(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		_timeline = timeline;
		MouseTracking = true;
		Cursor = CursorShape.SizeH;
		ToolTip = "Drag to scrub whole frames";
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( new Color( 0.075f, 0.083f, 0.092f ) );
		Paint.DrawRect( LocalRect );
		_timeline.DrawLabelGutter( Height );
		var clip = _timeline.Clip;
		if ( clip is null )
			return;

		var range = _timeline.VisibleRange;
		var bodyWidth = MathF.Max(
			_timeline.BodyRight( Width ) - TimelineEditorCanvas.GraphStartX,
			1 );
		var spacing = TimelineInteraction.TickSpacing( bodyWidth / MathF.Max( range.Span, 1 ) );
		var first = (int)MathF.Ceiling( range.StartFrame / (float)spacing.MinorFrames )
			* spacing.MinorFrames;
		for ( var frame = first; frame <= range.EndFrame; frame += spacing.MinorFrames )
		{
			var x = _timeline.FrameToX( frame, Width );
			var major = frame % spacing.MajorFrames == 0;
			Paint.SetPen( Color.White.WithAlpha( major ? 0.18f : 0.075f ) );
			Paint.DrawLine(
				new Vector2( x, major ? 8 : 15 ),
				new Vector2( x, Height ) );
			if ( !major )
				continue;

			Paint.SetDefaultFont( 9, 500 );
			Paint.SetPen( WeaponAnimatorTheme.Muted );
			Paint.DrawText(
				new Rect( x + 4, 0, 52, Height ),
				frame.ToString(),
				TextFlag.LeftCenter );
		}

		Paint.SetDefaultFont( 8, 550 );
		Paint.SetPen( WeaponAnimatorTheme.Muted );
		Paint.DrawText(
			new Rect( 8, 0, TimelineEditorCanvas.TrackHeaderWidth - 16, Height ),
			"FRAME",
			TextFlag.LeftCenter );

		var playheadFrame = TimelineInteraction.TimeToFrame(
			_timeline.Controller.Document.Workspace.TimelineTime,
			clip.SampleRate );
		var playhead = _timeline.FrameToX( playheadFrame, Width );
		if ( IsGraphX( playhead ) )
		{
			Paint.SetPen( WeaponAnimatorTheme.Coral, 1.5f );
			Paint.DrawLine( new Vector2( playhead, 0 ), new Vector2( playhead, Height ) );
			Paint.SetBrushAndPen( WeaponAnimatorTheme.Coral );
			Paint.DrawRect( new Rect( playhead - 3, 0, 6, 7 ), 1 );
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton
			|| e.LocalPosition.x < TimelineEditorCanvas.GraphStartX
			|| e.LocalPosition.x > _timeline.BodyRight( Width ) )
		{
			base.OnMousePress( e );
			return;
		}

		_scrubbing = true;
		Scrub( e.LocalPosition.x );
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !_scrubbing || (e.ButtonState & MouseButtons.Left) == 0 )
		{
			base.OnMouseMove( e );
			return;
		}
		Scrub( e.LocalPosition.x );
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( !_scrubbing || e.Button != MouseButtons.Left )
		{
			base.OnMouseReleased( e );
			return;
		}
		_scrubbing = false;
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
		base.OnMouseWheel( e );
	}

	private void Scrub( float x ) =>
		_timeline.Controller.SetTimelineFrame( _timeline.XToFrame( x, Width ) );

	private bool IsGraphX( float x ) =>
		x >= TimelineEditorCanvas.GraphStartX
		&& x <= _timeline.BodyRight( Width );
}

internal sealed class TimelineTrackArea : BaseScrollWidget
{
	private const float KeyHitRadius = 8;
	private const float DragThreshold = 3;
	private readonly TimelineEditorCanvas _timeline;
	private TimelineGridDragMode _dragMode;
	private Vector2 _pressPosition;
	private Vector2 _dragPosition;
	private float _marqueePressContentY;
	private float _marqueeDragContentY;
	private bool _dragStarted;
	private bool _additiveSelection;
	private bool _toggleSelection;
	private HashSet<Guid> _selectionAtPress = [];
	private Dictionary<Guid, float> _dragStartTimes = [];
	private int _dragStartFrame;
	private int _dragDeltaFrames;
	private Guid _restoredClipId;
	private bool _restoringScroll;
	private bool _lastCurveMode;

	public TimelineTrackArea(
		TimelineEditorCanvas timeline,
		Widget? parent = null ) : base( parent )
	{
		_timeline = timeline;
		MinimumSize = new Vector2( 320, 72 );
		MouseTracking = true;
		FocusMode = FocusMode.Click;
		HorizontalScrollbarMode = ScrollbarMode.Off;
		VerticalScrollbarMode = ScrollbarMode.Auto;
		SetStyles( "background-color: rgb(12,14,16); border: none;" );
	}

	[Shortcut( "weaponanim.timeline.delete", "DEL" )]
	private void ShortcutDelete() => _timeline.Controller.DeleteSelectedKeys();

	[Shortcut( "weaponanim.timeline.backspace", "BACKSPACE" )]
	private void ShortcutBackspace() => _timeline.Controller.DeleteSelectedKeys();

	public void Refresh()
	{
		ConfigureScrollbar();
		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();
		Paint.SetBrushAndPen( WeaponAnimatorTheme.Background );
		Paint.DrawRect( LocalRect );
		_timeline.DrawLabelGutter( Height );
		var clip = _timeline.Clip;
		if ( clip is null )
			return;

		ConfigureScrollbar();
		DrawGrid( clip );
		if (_timeline.Controller.Document.Workspace.CurveEditorVisible )
			DrawCurves( clip );
		else
			DrawDopeSheet( clip );

		var playheadFrame = TimelineInteraction.TimeToFrame(
			_timeline.Controller.Document.Workspace.TimelineTime,
			clip.SampleRate );
		var playheadX = _timeline.FrameToX( playheadFrame, Width );
		if ( IsGraphX( playheadX ) )
		{
			Paint.SetPen( WeaponAnimatorTheme.Coral, 1.5f );
			Paint.DrawLine(
				new Vector2( playheadX, 0 ),
				new Vector2( playheadX, Height ) );
		}

		if ( _dragMode == TimelineGridDragMode.Marquee )
		{
			var selection = SelectionRect();
			Paint.SetBrushAndPen( WeaponAnimatorTheme.Cyan.WithAlpha( 0.12f ) );
			Paint.DrawRect( selection, 1 );
			Paint.ClearBrush();
			Paint.SetPen( WeaponAnimatorTheme.Cyan.WithAlpha( 0.9f ) );
			Paint.DrawRect( selection, 1 );
		}

		var gutterLeft = _timeline.BodyRight( Width );
		Paint.SetBrushAndPen( new Color( 0.055f, 0.061f, 0.068f ) );
		Paint.DrawRect( new Rect( gutterLeft, 0, Width - gutterLeft, Height ) );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton
			|| _timeline.Clip is not { } clip
			|| _timeline.Controller.Document.Workspace.CurveEditorVisible
			|| e.LocalPosition.x < TimelineEditorCanvas.GraphStartX
			|| e.LocalPosition.x > _timeline.BodyRight( Width ) )
		{
			base.OnMousePress( e );
			return;
		}

		_pressPosition = _dragPosition = e.LocalPosition;
		_marqueePressContentY = _marqueeDragContentY =
			e.LocalPosition.y + VerticalScrollbar.Value;
		_dragStarted = false;
		_additiveSelection = e.HasShift;
		_toggleSelection = e.HasCtrl;
		_selectionAtPress = _timeline.Controller.SelectedKeys.ToHashSet();

		var playheadFrame = TimelineInteraction.TimeToFrame(
			_timeline.Controller.Document.Workspace.TimelineTime,
			clip.SampleRate );
		if ( MathF.Abs(
			_timeline.FrameToX( playheadFrame, Width )
			- e.LocalPosition.x ) <= 5 )
		{
			_dragMode = TimelineGridDragMode.Playhead;
			_timeline.Controller.SetTimelineFrame(
				_timeline.XToFrame( e.LocalPosition.x, Width ) );
			e.Accepted = true;
			return;
		}

		var hit = HitKey( clip, e.LocalPosition );
		if ( hit is null )
		{
			_dragMode = TimelineGridDragMode.Marquee;
			e.Accepted = true;
			return;
		}

		if ( _toggleSelection )
		{
			_timeline.Controller.ToggleSelectedKeys( [hit.Value.Id] );
			if ( !_timeline.Controller.SelectedKeys.Contains( hit.Value.Id ) )
			{
				_dragMode = TimelineGridDragMode.None;
				e.Accepted = true;
				return;
			}
		}
		else if ( _additiveSelection )
		{
			_timeline.Controller.SelectKeys( [hit.Value.Id], true );
		}
		else if ( !_timeline.Controller.SelectedKeys.Contains( hit.Value.Id ) )
		{
			_timeline.Controller.SetSelectedKeys( [hit.Value.Id] );
		}

		_dragMode = TimelineGridDragMode.Keys;
		_dragStartFrame = _timeline.XToFrame( e.LocalPosition.x, Width );
		_dragStartTimes = SelectedKeyTimes( clip );
		_timeline.Controller.BeginSelectedKeyMove();
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _dragMode == TimelineGridDragMode.None
			|| (e.ButtonState & MouseButtons.Left) == 0
			|| _timeline.Clip is not { } clip )
		{
			base.OnMouseMove( e );
			return;
		}

		_dragPosition = e.LocalPosition;
		if ( _dragMode == TimelineGridDragMode.Marquee )
			_marqueeDragContentY = e.LocalPosition.y + VerticalScrollbar.Value;
		_dragStarted |= (_dragPosition - _pressPosition).Length >= DragThreshold;
		switch ( _dragMode )
		{
			case TimelineGridDragMode.Playhead:
				_timeline.Controller.SetTimelineFrame(
					_timeline.XToFrame( e.LocalPosition.x, Width ) );
				break;
			case TimelineGridDragMode.Marquee:
				ApplyMarqueeSelection( clip );
				Update();
				break;
			case TimelineGridDragMode.Keys when _dragStarted:
				var requested = _timeline.XToFrame( e.LocalPosition.x, Width )
					- _dragStartFrame;
				var startFrames = _dragStartTimes.Values.Select( x =>
					TimelineInteraction.TimeToFrame( x, clip.SampleRate ) );
				var delta = TimelineInteraction.ClampGroupFrameDelta(
					startFrames,
					requested,
					TimelineInteraction.LastFrame( clip ) );
				if ( delta == _dragDeltaFrames )
					break;
				_dragDeltaFrames = delta;
				_timeline.Controller.UpdateSelectedKeyMove(
					_dragStartTimes,
					_dragDeltaFrames );
				Update();
				break;
		}
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( _dragMode == TimelineGridDragMode.None || e.Button != MouseButtons.Left )
		{
			base.OnMouseReleased( e );
			return;
		}

		var clip = _timeline.Clip;
		if ( _dragMode == TimelineGridDragMode.Marquee && clip is not null )
		{
			_dragPosition = e.LocalPosition;
			_marqueeDragContentY = e.LocalPosition.y + VerticalScrollbar.Value;
			ApplyMarqueeSelection( clip );
		}
		else if ( _dragMode == TimelineGridDragMode.Keys )
		{
			if ( _dragStarted )
				_timeline.Controller.EndSelectedKeyMove(
					_dragStartTimes,
					_dragDeltaFrames );
			else
				_timeline.Controller.EndContinuousEdit();
		}

		_dragMode = TimelineGridDragMode.None;
		_dragStartTimes.Clear();
		_dragDeltaFrames = 0;
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
		base.OnMouseWheel( e );
	}

	protected override void OnScrollChanged()
	{
		base.OnScrollChanged();
		if ( _restoringScroll
			|| _timeline.Clip is not { } clip
			|| _timeline.Controller.Document.Workspace.CurveEditorVisible )
			return;
		if ( _dragMode == TimelineGridDragMode.Marquee )
		{
			_marqueeDragContentY = _dragPosition.y + VerticalScrollbar.Value;
			ApplyMarqueeSelection( clip );
		}
		_timeline.Controller.SetTimelineVerticalScroll(
			clip,
			VerticalScrollbar.Value );
		Update();
	}

	private void ConfigureScrollbar()
	{
		var clip = _timeline.Clip;
		if ( clip is null )
			return;
		var curves = _timeline.Controller.Document.Workspace.CurveEditorVisible;
		var rowCount = curves ? 1 : TimelineRows( clip );
		var maximum = Math.Max(
			0,
			(int)MathF.Ceiling( rowCount * TimelineEditorCanvas.TrackHeight - Height ) );
		_restoringScroll = true;
		VerticalScrollbar.Minimum = 0;
		VerticalScrollbar.Maximum = maximum;
		VerticalScrollbar.SingleStep = (int)TimelineEditorCanvas.TrackHeight;
		VerticalScrollbar.PageStep = Math.Max( (int)Height, 1 );
		if ( _restoredClipId != clip.Id || _lastCurveMode != curves )
		{
			_restoredClipId = clip.Id;
			_lastCurveMode = curves;
			var saved = curves
				? 0
				: _timeline.Controller.Document.Workspace
					.GetTimelineView( clip.Id )?.VerticalScroll ?? 0;
			VerticalScrollbar.Value = Math.Clamp( (int)saved, 0, maximum );
		}
		_restoringScroll = false;
	}

	private void DrawGrid( WeaponAnimationClip clip )
	{
		var range = _timeline.VisibleRange;
		var bodyWidth = MathF.Max(
			_timeline.BodyRight( Width ) - TimelineEditorCanvas.GraphStartX,
			1 );
		var spacing = TimelineInteraction.TickSpacing( bodyWidth / MathF.Max( range.Span, 1 ) );
		var first = (int)MathF.Ceiling( range.StartFrame / (float)spacing.MinorFrames )
			* spacing.MinorFrames;
		for ( var frame = first; frame <= range.EndFrame; frame += spacing.MinorFrames )
		{
			var x = _timeline.FrameToX( frame, Width );
			var major = frame % spacing.MajorFrames == 0;
			Paint.SetPen( Color.White.WithAlpha( major ? 0.10f : 0.035f ) );
			Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Height ) );
		}
	}

	private void DrawDopeSheet( WeaponAnimationClip clip )
	{
		var parts = _timeline.Controller.Document.Rig.VisibilityParts;
		for ( var index = 0; index < parts.Count; index++ )
			DrawVisibilityRow( clip, parts[index], index );
		for ( var index = 0; index < clip.Tracks.Count; index++ )
			DrawTransformRow( clip, clip.Tracks[index], parts.Count + index );
		DrawTagRow( clip, parts.Count + clip.Tracks.Count );
	}

	private void DrawTransformRow(
		WeaponAnimationClip clip,
		TransformTrack track,
		int rowIndex )
	{
		var y = RowY( rowIndex );
		if ( y + TimelineEditorCanvas.TrackHeight < 0 || y > Height )
			return;
		DrawRowBackground( y, rowIndex );
		Paint.SetPen( TrackColor( track.Kind ) );
		Paint.DrawText(
			new Rect(
				8,
				y,
				TimelineEditorCanvas.TrackHeaderWidth - 12,
				TimelineEditorCanvas.TrackHeight ),
			track.Target,
			TextFlag.LeftCenter );
		foreach ( var key in track.Keys )
			DrawKey(
				key.Id,
				key.Time * clip.SampleRate,
				y,
				TrackColor( track.Kind ) );
	}

	private void DrawVisibilityRow(
		WeaponAnimationClip clip,
		WeaponVisibilityPart part,
		int rowIndex )
	{
		var y = RowY( rowIndex );
		if ( y + TimelineEditorCanvas.TrackHeight < 0 || y > Height )
			return;
		DrawRowBackground( y, rowIndex );
		Paint.SetPen( WeaponAnimatorTheme.Amber );
		Paint.DrawText(
			new Rect(
				8,
				y,
				TimelineEditorCanvas.TrackHeaderWidth - 12,
				TimelineEditorCanvas.TrackHeight ),
			$"◉  {part.Name}",
			TextFlag.LeftCenter );

		foreach ( var span in WeaponVisibilityEvaluator.BuildSpans( part, clip ) )
		{
			var start = _timeline.FrameToX( span.StartTime * clip.SampleRate, Width );
			var end = _timeline.FrameToX( span.EndTime * clip.SampleRate, Width );
			if ( end < TimelineEditorCanvas.GraphStartX
				|| start > _timeline.BodyRight( Width ) )
				continue;
			start = MathF.Max( start, TimelineEditorCanvas.GraphStartX );
			end = MathF.Min( end, _timeline.BodyRight( Width ) );
			var color = span.Visible
				? WeaponAnimatorTheme.Green
				: WeaponAnimatorTheme.Coral;
			Paint.SetBrushAndPen( color.WithAlpha( span.Visible ? 0.28f : 0.24f ) );
			Paint.DrawRect(
				new Rect(
					start,
					y + 4,
					MathF.Max( end - start, 1 ),
					TimelineEditorCanvas.TrackHeight - 8 ),
				2 );
		}

		var track = clip.VisibilityTracks.FirstOrDefault( x => x.PartId == part.Id );
		if ( track is null )
			return;
		foreach ( var key in track.Keys )
			DrawKey(
				key.Id,
				key.Time * clip.SampleRate,
				y,
				key.Visible ? WeaponAnimatorTheme.Green : WeaponAnimatorTheme.Coral );
	}

	private void DrawTagRow( WeaponAnimationClip clip, int rowIndex )
	{
		var y = RowY( rowIndex );
		if ( y + TimelineEditorCanvas.TrackHeight < 0 || y > Height )
			return;
		DrawRowBackground( y, rowIndex );
		Paint.SetPen( WeaponAnimatorTheme.Muted );
		Paint.DrawText(
			new Rect(
				8,
				y,
				TimelineEditorCanvas.TrackHeaderWidth - 12,
				TimelineEditorCanvas.TrackHeight ),
			"TAGS",
			TextFlag.LeftCenter );
		foreach ( var tag in clip.Tags )
		{
			var start = _timeline.FrameToX( tag.StartTime * clip.SampleRate, Width );
			var end = _timeline.FrameToX( tag.EndTime * clip.SampleRate, Width );
			if ( tag.Kind == AnimationTagKind.Point && !IsGraphX( start ) )
				continue;
			if ( tag.Kind == AnimationTagKind.Range
				&& (end < TimelineEditorCanvas.GraphStartX
					|| start > _timeline.BodyRight( Width )) )
				continue;
			start = Math.Clamp(
				start,
				TimelineEditorCanvas.GraphStartX,
				_timeline.BodyRight( Width ) );
			end = Math.Clamp(
				end,
				TimelineEditorCanvas.GraphStartX,
				_timeline.BodyRight( Width ) );
			Paint.SetBrushAndPen( WeaponAnimatorTheme.Green.WithAlpha( 0.65f ) );
			if ( tag.Kind == AnimationTagKind.Point )
				Paint.DrawRect(
					new Rect(
						start - 2,
						y + 4,
						4,
						TimelineEditorCanvas.TrackHeight - 8 ),
					1 );
			else
				Paint.DrawRect(
					new Rect(
						start,
						y + 5,
						MathF.Max( end - start, 4 ),
						TimelineEditorCanvas.TrackHeight - 10 ),
					2 );
		}
	}

	private void DrawCurves( WeaponAnimationClip clip )
	{
		var tracks = clip.Tracks.Where( x => x.Keys.Count > 0 ).Take( 3 ).ToArray();
		if ( tracks.Length == 0 )
			return;
		var graph = new Rect(
			TimelineEditorCanvas.GraphStartX,
			0,
			_timeline.BodyRight( Width ) - TimelineEditorCanvas.GraphStartX,
			Height );
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
				new Rect(
					8,
					index * 20,
					TimelineEditorCanvas.TrackHeaderWidth - 12,
					20 ),
				track.Target,
				TextFlag.LeftCenter );
			Vector2? previous = null;
			foreach ( var key in track.Keys.OrderBy( x => x.Time ) )
			{
				var point = new Vector2(
					_timeline.FrameToX( key.Time * clip.SampleRate, Width ),
					graph.Bottom - (key.Position.x - minimum)
						/ (maximum - minimum) * graph.Height );
				if ( !IsGraphX( point.x ) )
				{
					previous = null;
					continue;
				}
				if ( previous is not null )
					Paint.DrawLine( previous.Value, point );
				Paint.DrawCircle( point, 3 );
				previous = point;
			}
		}
	}

	private void DrawRowBackground( float y, int rowIndex )
	{
		Paint.SetBrushAndPen( rowIndex % 2 == 0
			? Color.White.WithAlpha( 0.018f )
			: Color.Transparent );
		Paint.DrawRect(
			new Rect(
				0,
				y,
				_timeline.BodyRight( Width ),
				TimelineEditorCanvas.TrackHeight ) );
	}

	private void DrawKey( Guid id, float frame, float y, Color color )
	{
		var x = _timeline.FrameToX( frame, Width );
		if ( !IsGraphX( x ) )
			return;
		var selected = _timeline.Controller.SelectedKeys.Contains( id );
		Paint.SetBrushAndPen( selected ? Color.White : color );
		Paint.DrawPolygon(
		[
			new Vector2( x, y + 5 ),
			new Vector2( x + 5, y + TimelineEditorCanvas.TrackHeight * 0.5f ),
			new Vector2( x, y + TimelineEditorCanvas.TrackHeight - 5 ),
			new Vector2( x - 5, y + TimelineEditorCanvas.TrackHeight * 0.5f )
		] );
	}

	private TimelineKeyPoint? HitKey( WeaponAnimationClip clip, Vector2 position )
	{
		var row = (int)MathF.Floor(
			(position.y + VerticalScrollbar.Value)
			/ TimelineEditorCanvas.TrackHeight );
		foreach ( var key in KeyPoints( clip )
			.Where( x => x.Row == row )
			.OrderBy( x => MathF.Abs( x.Position.x - position.x ) ) )
		{
			if ( MathF.Abs( key.Position.x - position.x ) <= KeyHitRadius )
				return key;
		}
		return null;
	}

	private IEnumerable<TimelineKeyPoint> KeyPoints( WeaponAnimationClip clip )
	{
		var parts = _timeline.Controller.Document.Rig.VisibilityParts;
		for ( var row = 0; row < parts.Count; row++ )
		{
			var track = clip.VisibilityTracks.FirstOrDefault( x =>
				x.PartId == parts[row].Id );
			if ( track is null )
				continue;
			foreach ( var key in track.Keys )
				yield return new TimelineKeyPoint(
					key.Id,
					key.Time,
					row,
					new Vector2(
						_timeline.FrameToX( key.Time * clip.SampleRate, Width ),
						RowY( row ) + TimelineEditorCanvas.TrackHeight * 0.5f ) );
		}

		for ( var index = 0; index < clip.Tracks.Count; index++ )
		{
			var row = parts.Count + index;
			foreach ( var key in clip.Tracks[index].Keys )
				yield return new TimelineKeyPoint(
					key.Id,
					key.Time,
					row,
					new Vector2(
						_timeline.FrameToX( key.Time * clip.SampleRate, Width ),
						RowY( row ) + TimelineEditorCanvas.TrackHeight * 0.5f ) );
		}
	}

	private Dictionary<Guid, float> SelectedKeyTimes( WeaponAnimationClip clip ) =>
		clip.Tracks.SelectMany( x => x.Keys )
			.Select( x => (x.Id, x.Time) )
			.Concat( clip.VisibilityTracks.SelectMany( x => x.Keys )
				.Select( x => (x.Id, x.Time) ) )
			.Where( x => _timeline.Controller.SelectedKeys.Contains( x.Id ) )
			.ToDictionary( x => x.Id, x => x.Time );

	private void ApplyMarqueeSelection( WeaponAnimationClip clip )
	{
		var selection = SelectionRect();
		var hits = KeyPoints( clip )
			.Where( x => Contains( selection, x.Position ) )
			.Select( x => x.Id )
			.ToHashSet();
		var result = TimelineInteraction.CombineKeySelection(
			_selectionAtPress,
			hits,
			_additiveSelection,
			_toggleSelection );
		_timeline.Controller.SetSelectedKeys( result );
	}

	private Rect SelectionRect()
	{
		var bounds = TimelineInteraction.ProjectMarquee(
			_pressPosition.x,
			_marqueePressContentY,
			_dragPosition.x,
			_marqueeDragContentY,
			VerticalScrollbar.Value,
			TimelineEditorCanvas.GraphStartX,
			_timeline.BodyRight( Width ) );
		return new Rect(
			bounds.Left,
			bounds.Top,
			bounds.Right - bounds.Left,
			bounds.Bottom - bounds.Top );
	}

	private float RowY( int row ) =>
		row * TimelineEditorCanvas.TrackHeight - VerticalScrollbar.Value;

	private int TimelineRows( WeaponAnimationClip clip ) =>
		TimelineInteraction.TrackRowCount(
			_timeline.Controller.Document,
			clip );

	private static bool Contains( Rect rect, Vector2 point ) =>
		point.x >= rect.Left
		&& point.x <= rect.Right
		&& point.y >= rect.Top
		&& point.y <= rect.Bottom;

	private bool IsGraphX( float x ) =>
		x >= TimelineEditorCanvas.GraphStartX
		&& x <= _timeline.BodyRight( Width );

	private static Color TrackColor( RigControlKind kind ) => kind switch
	{
		RigControlKind.Weapon => WeaponAnimatorTheme.Amber,
		RigControlKind.Camera => WeaponAnimatorTheme.Green,
		_ => WeaponAnimatorTheme.Cyan
	};

	private readonly record struct TimelineKeyPoint(
		Guid Id,
		float Time,
		int Row,
		Vector2 Position );

	private enum TimelineGridDragMode
	{
		None,
		Marquee,
		Keys,
		Playhead
	}
}
