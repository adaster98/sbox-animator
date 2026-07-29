#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class WeaponAnimatorController
{
	private const int MaximumHistory = 256;
	private static string KeyClipboard = "";
	private readonly List<DocumentSnapshot> _undo = [];
	private readonly List<DocumentSnapshot> _redo = [];
	private string? _continuousBefore;
	private string _continuousDescription = "";
	private bool _continuousWasDirty;
	private bool _isPlaying;
	private float _playbackTime;

	public WeaponAnimationDocument Document { get; private set; } = WeaponAnimationDocument.CreateDefault();
	public bool IsDirty { get; private set; }
	public string LastAction { get; private set; } = "";
	public IReadOnlyCollection<Guid> SelectedKeys => _selectedKeys;
	public bool IsPlaying => _isPlaying;

	private readonly HashSet<Guid> _selectedKeys = [];

	public event Action? DocumentChanged;
	public event Action? PoseChanged;
	public event Action? SelectionChanged;
	public event Action? KeySelectionChanged;
	public event Action? DirtyChanged;
	public event Action? TimelineChanged;
	public event Action? TimelineViewChanged;
	public event Action? PlaybackChanged;

	public bool CanUndo => _undo.Count > 0;
	public bool CanRedo => _redo.Count > 0;

	public void SetDocument( WeaponAnimationDocument document )
	{
		_continuousBefore = null;
		_continuousDescription = "";
		Document = document ?? WeaponAnimationDocument.CreateDefault();
		_undo.Clear();
		_redo.Clear();
		_selectedKeys.Clear();
		_isPlaying = false;
		_playbackTime = 0;
		IsDirty = false;
		LastAction = "";
		DocumentChanged?.Invoke();
		SelectionChanged?.Invoke();
		KeySelectionChanged?.Invoke();
		DirtyChanged?.Invoke();
		PlaybackChanged?.Invoke();
	}

	public void Mutate( string description, Action<WeaponAnimationDocument> mutation )
	{
		EndContinuousEdit();
		var before = Serialize( Document );
		mutation( Document );
		var after = Serialize( Document );
		if ( before == after )
			return;

		_undo.Add( new DocumentSnapshot( description, before ) );
		if ( _undo.Count > MaximumHistory )
			_undo.RemoveAt( 0 );
		_redo.Clear();
		LastAction = description;
		SetDirty( true );
		DocumentChanged?.Invoke();
	}

	public void UpdateWorkspacePreference(
		string description,
		Action<WorkspaceState> mutation )
	{
		EndContinuousEdit();
		var before = Json.Serialize( Document.Workspace );
		mutation( Document.Workspace );
		if ( before == Json.Serialize( Document.Workspace ) )
			return;

		// Viewport-only preferences are read every frame and do not need a full panel rebuild.
		_redo.Clear();
		LastAction = description;
		SetDirty( true );
	}

	public void MarkWorkspacePreferenceChanged( string description )
	{
		LastAction = description;
		SetDirty( true );
	}

	public void BeginContinuousEdit( string description )
	{
		EndContinuousEdit();
		_continuousBefore = Serialize( Document );
		_continuousDescription = description;
		_continuousWasDirty = IsDirty;
	}

	public void UpdateContinuousEdit( Action<WeaponAnimationDocument> mutation )
	{
		if ( _continuousBefore is null )
			return;

		mutation( Document );
		LastAction = _continuousDescription;
		SetDirty( true );
		PoseChanged?.Invoke();
	}

	public void EndContinuousEdit()
	{
		if ( _continuousBefore is null )
			return;

		var before = _continuousBefore;
		var description = _continuousDescription;
		_continuousBefore = null;
		_continuousDescription = "";
		var after = Serialize( Document );
		if ( before == after )
		{
			SetDirty( _continuousWasDirty );
			return;
		}

		_undo.Add( new DocumentSnapshot( description, before ) );
		if ( _undo.Count > MaximumHistory )
			_undo.RemoveAt( 0 );
		_redo.Clear();
		LastAction = description;
		SetDirty( true );
		DocumentChanged?.Invoke();
	}

	public void ReplaceWithoutHistory( WeaponAnimationDocument document, bool dirty )
	{
		Document = document;
		SetDirty( dirty );
		DocumentChanged?.Invoke();
		SelectionChanged?.Invoke();
		KeySelectionChanged?.Invoke();
	}

	public void Undo()
	{
		EndContinuousEdit();
		SetPlaying( false );
		if ( _undo.Count == 0 )
			return;

		var snapshot = _undo[^1];
		_undo.RemoveAt( _undo.Count - 1 );
		_redo.Add( new DocumentSnapshot( snapshot.Description, Serialize( Document ) ) );
		Document = Deserialize( snapshot.Json );
		LastAction = $"Undo {snapshot.Description}";
		SetDirty( true );
		DocumentChanged?.Invoke();
		SelectionChanged?.Invoke();
		KeySelectionChanged?.Invoke();
	}

	public void Redo()
	{
		EndContinuousEdit();
		SetPlaying( false );
		if ( _redo.Count == 0 )
			return;

		var snapshot = _redo[^1];
		_redo.RemoveAt( _redo.Count - 1 );
		_undo.Add( new DocumentSnapshot( snapshot.Description, Serialize( Document ) ) );
		Document = Deserialize( snapshot.Json );
		LastAction = $"Redo {snapshot.Description}";
		SetDirty( true );
		DocumentChanged?.Invoke();
		SelectionChanged?.Invoke();
		KeySelectionChanged?.Invoke();
	}

	public void MarkSaved()
	{
		SetDirty( false );
	}

	public void SelectBone( string name )
	{
		if ( Document.Workspace.SelectedBone == name )
			return;

		Document.Workspace.SelectedBone = name ?? "";
		Document.Workspace.SelectedControl = "";
		SelectionChanged?.Invoke();
	}

	public void SelectControl( string name )
	{
		if ( Document.Workspace.SelectedControl == name )
			return;

		Document.Workspace.SelectedControl = name ?? "";
		Document.Workspace.SelectedBone = "";
		SelectionChanged?.Invoke();
	}

	public void SelectClip( Guid clipId )
	{
		if ( Document.Workspace.SelectedClipId == clipId )
			return;

		Document.Workspace.SelectedClipId = clipId;
		Document.Workspace.TimelineTime = 0;
		_selectedKeys.Clear();
		SetPlaying( false );
		SelectionChanged?.Invoke();
		KeySelectionChanged?.Invoke();
		DocumentChanged?.Invoke();
	}

	public void SetTimelineTime( float time )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;
		SetPlaying( false );
		SetTimelineFrameInternal(
			TimelineInteraction.TimeToFrame( time, clip.SampleRate ),
			clip );
	}

	public void SetTimelineFrame( int frame )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;
		SetPlaying( false );
		SetTimelineFrameInternal( frame, clip );
	}

	public void StepTimelineFrame( int delta )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;
		var current = TimelineInteraction.TimeToFrame(
			Document.Workspace.TimelineTime,
			clip.SampleRate );
		SetTimelineFrame( current + delta );
	}

	public void JumpToFirstFrame() => SetTimelineFrame( 0 );

	public void JumpToLastFrame()
	{
		var clip = Document.GetSelectedClip();
		if ( clip is not null )
			SetTimelineFrame( TimelineInteraction.LastFrame( clip ) );
	}

	public void TogglePlayback()
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;
		if ( _isPlaying )
		{
			SetPlaying( false );
			return;
		}

		var frame = TimelineInteraction.TimeToFrame(
			Document.Workspace.TimelineTime,
			clip.SampleRate );
		if ( frame >= TimelineInteraction.LastFrame( clip ) )
			SetTimelineFrameInternal( 0, clip );
		_playbackTime = Document.Workspace.TimelineTime;
		SetPlaying( true );
	}

	public void PausePlayback() => SetPlaying( false );

	public void AdvancePlayback( float delta )
	{
		var clip = Document.GetSelectedClip();
		if ( !_isPlaying || clip is null || clip.Duration <= 0 )
			return;

		_playbackTime += MathF.Max( delta, 0 );
		if ( _playbackTime > clip.Duration )
		{
			if ( clip.Loop )
				_playbackTime %= clip.Duration;
			else
			{
				_playbackTime = TimelineInteraction.FrameToTime(
					TimelineInteraction.LastFrame( clip ),
					clip.SampleRate );
				SetPlaying( false );
			}
		}

		SetTimelineFrameInternal(
			TimelineInteraction.TimeToFrame( _playbackTime, clip.SampleRate ),
			clip,
			syncPlaybackTime: false );
	}

	internal TimelineFrameRange GetTimelineRange( WeaponAnimationClip clip ) =>
		TimelineInteraction.ResolveRange(
			clip,
			Document.Workspace.GetTimelineView( clip.Id ) );

	internal void SetTimelineRange( WeaponAnimationClip clip, TimelineFrameRange range )
	{
		var current = GetTimelineRange( clip );
		if ( current == range )
			return;

		var state = Document.Workspace.EnsureTimelineView( clip.Id, clip.Duration );
		state.VisibleStart = TimelineInteraction.FrameToTime(
			range.StartFrame,
			clip.SampleRate );
		state.VisibleEnd = TimelineInteraction.FrameToTime(
			range.EndFrame,
			clip.SampleRate );
		LastAction = "Timeline zoom";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void SetTimelineVerticalScroll( WeaponAnimationClip clip, float value )
	{
		var state = Document.Workspace.EnsureTimelineView( clip.Id, clip.Duration );
		value = MathF.Max( value, 0 );
		if ( MathF.Abs( state.VerticalScroll - value ) <= 0.5f )
			return;

		state.VerticalScroll = value;
		LastAction = "Timeline scroll";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void SetCurveEditorVisible( bool visible )
	{
		if ( Document.Workspace.CurveEditorVisible == visible )
			return;
		Document.Workspace.CurveEditorVisible = visible;
		if ( visible && Document.GetSelectedClip() is { } clip )
			CurveEditingService.ResolveSelectedTrack( Document, clip );
		LastAction = visible ? "Open curve editor" : "Open key editor";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void SelectCurveTrack( WeaponAnimationClip clip, Guid trackId )
	{
		var track = clip.Tracks.FirstOrDefault( x => x.Id == trackId && x.Keys.Count > 0 );
		if ( track is null )
			return;
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		if ( view.SelectedTrackId == trackId )
			return;
		view.SelectedTrackId = trackId;
		view.VisibleChannels = CurveEditingService.DefaultVisibleChannels( track );
		view.HasVerticalRange = false;
		if ( !track.Target.StartsWith( "@", StringComparison.Ordinal ) )
		{
			Document.Workspace.SelectedBone = track.Target;
			Document.Workspace.SelectedControl = "";
		}
		else
		{
			Document.Workspace.SelectedControl = track.Target;
			Document.Workspace.SelectedBone = "";
		}
		LastAction = $"Curve track {track.Target}";
		SetDirty( true );
		SelectionChanged?.Invoke();
		TimelineViewChanged?.Invoke();
	}

	internal void SetCurveMode( WeaponAnimationClip clip, CurveEditorMode mode )
	{
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		if ( view.Mode == mode )
			return;
		view.Mode = mode;
		view.HasVerticalRange = false;
		LastAction = $"Curve mode {mode}";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void SetCurveChannels(
		WeaponAnimationClip clip,
		TransformCurveChannel channels )
	{
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		channels &= TransformCurveChannel.All;
		if ( channels == TransformCurveChannel.None || view.VisibleChannels == channels )
			return;
		view.VisibleChannels = channels;
		view.HasVerticalRange = false;
		LastAction = "Curve channels";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void SetCurveSearch( WeaponAnimationClip clip, string search )
	{
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		search ??= "";
		if ( view.Search == search )
			return;
		view.Search = search;
		view.TrackScroll = 0;
		LastAction = "Curve search";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void SetCurveTrackScroll( WeaponAnimationClip clip, float value )
	{
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		value = MathF.Max( value, 0 );
		if ( MathF.Abs( view.TrackScroll - value ) <= 0.5f )
			return;
		view.TrackScroll = value;
		LastAction = "Curve track scroll";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void SetCurveVerticalRange(
		WeaponAnimationClip clip,
		float minimum,
		float maximum )
	{
		if ( !WeaponAnimationMath.IsFinite( minimum )
			|| !WeaponAnimationMath.IsFinite( maximum )
			|| maximum - minimum < 0.0001f )
			return;
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		view.HasVerticalRange = true;
		view.VerticalMinimum = minimum;
		view.VerticalMaximum = maximum;
		LastAction = "Curve vertical range";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void FitCurveVerticalRange( WeaponAnimationClip clip )
	{
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		if ( !view.HasVerticalRange )
			return;
		view.HasVerticalRange = false;
		LastAction = "Fit curves";
		SetDirty( true );
		TimelineViewChanged?.Invoke();
	}

	internal void ApplyCurvePreset( CurvePreset preset )
	{
		var clip = Document.GetSelectedClip();
		var track = clip is null
			? null
			: CurveEditingService.ResolveSelectedTrack( Document, clip );
		if ( clip is null || track is null || track.Keys.Count < 2 )
			return;
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		Mutate( $"Curve {preset}", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			CurveEditingService.ApplyPreset(
				track,
				_selectedKeys,
				view.Mode,
				view.VisibleChannels,
				preset );
			CurveEditingService.RepairTrack( track );
		} );
	}

	internal void AlignSelectedCurveHandles()
		=> SetSelectedCurveHandleMode( CurveHandleMode.Aligned );

	internal void SetSelectedCurveHandleMode( CurveHandleMode mode )
	{
		var clip = Document.GetSelectedClip();
		var track = clip is null
			? null
			: CurveEditingService.ResolveSelectedTrack( Document, clip );
		if ( clip is null || track is null )
			return;
		var view = Document.Workspace.EnsureCurveView( clip.Id );
		var keys = track.Keys.Where( x => _selectedKeys.Contains( x.Id ) ).ToArray();
		if ( keys.Length == 0 )
			return;
		Mutate(
			mode == CurveHandleMode.Aligned
				? "Align curve handles"
				: "Free curve handles",
			_ =>
		{
			if ( view.Mode == CurveEditorMode.Speed )
			{
				foreach ( var span in track.CurveSpans.Where( x => x.HasSpeedCurve ) )
				{
					if ( _selectedKeys.Contains( span.StartKeyId ) )
						span.Speed.StartHandleMode = mode;
					if ( _selectedKeys.Contains( span.EndKeyId ) )
						span.Speed.EndHandleMode = mode;
				}
			}
			else
			{
				foreach ( var key in keys )
				{
					if ( mode == CurveHandleMode.Aligned )
						CurveEditingService.AlignHandles( key, view.VisibleChannels );
					else
						CurveEditingService.FreeHandles( key, view.VisibleChannels );
				}
			}
		} );
	}

	public void SelectKeys( IEnumerable<Guid> keyIds, bool additive )
	{
		if ( !additive )
			_selectedKeys.Clear();

		foreach ( var key in keyIds )
			_selectedKeys.Add( key );
		KeySelectionChanged?.Invoke();
	}

	public void SetSelectedKeys( IEnumerable<Guid> keyIds )
	{
		var next = keyIds.ToHashSet();
		if ( _selectedKeys.SetEquals( next ) )
			return;
		_selectedKeys.Clear();
		foreach ( var keyId in next )
			_selectedKeys.Add( keyId );
		KeySelectionChanged?.Invoke();
	}

	public void ToggleSelectedKeys( IEnumerable<Guid> keyIds )
	{
		foreach ( var keyId in keyIds )
		{
			if ( !_selectedKeys.Remove( keyId ) )
				_selectedKeys.Add( keyId );
		}
		KeySelectionChanged?.Invoke();
	}

	public void BeginSelectedKeyMove()
	{
		if ( _selectedKeys.Count > 0 )
		{
			SetPlaying( false );
			BeginContinuousEdit( "Move keys" );
		}
	}

	public void UpdateSelectedKeyMove(
		IReadOnlyDictionary<Guid, float> startTimes,
		int deltaFrames )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null || startTimes.Count == 0 )
			return;

		UpdateContinuousEdit( _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			ApplySelectedKeyMove( clip, startTimes, deltaFrames );
		} );
	}

	public void EndSelectedKeyMove(
		IReadOnlyDictionary<Guid, float> startTimes,
		int deltaFrames )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null || startTimes.Count == 0 )
		{
			EndContinuousEdit();
			return;
		}

		UpdateContinuousEdit( _ =>
		{
			ApplySelectedKeyMove( clip, startTimes, deltaFrames );
			RemoveKeyCollisions( clip );
		} );
		EndContinuousEdit();
	}

	internal void EndCurvePointMove()
	{
		var clip = Document.GetSelectedClip();
		if ( clip is not null )
		{
			UpdateContinuousEdit( _ =>
			{
				RemoveKeyCollisions( clip );
				foreach ( var track in clip.Tracks )
				{
					track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
					CurveEditingService.RepairTrack( track );
				}
			} );
		}
		EndContinuousEdit();
	}

	public void CopySelectedKeys()
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null || _selectedKeys.Count == 0 )
			return;

		var payload = new KeyClipboardPayload
		{
			Origin = clip.Tracks.SelectMany( x => x.Keys )
				.Select( x => (x.Id, x.Time) )
				.Concat( clip.VisibilityTracks.SelectMany( x => x.Keys )
					.Select( x => (x.Id, x.Time) ) )
				.Where( x => _selectedKeys.Contains( x.Id ) )
				.Select( x => x.Time )
				.DefaultIfEmpty( 0 )
				.Min()
		};

		foreach ( var track in clip.Tracks )
		{
			var keys = track.Keys.Where( x => _selectedKeys.Contains( x.Id ) ).ToList();
			if ( keys.Count == 0 )
				continue;

			payload.Tracks.Add( new ClipboardTrack
			{
				Target = track.Target,
				Kind = track.Kind,
				Interpolation = track.Interpolation,
				Keys = keys,
				CurveSpans = track.CurveSpans
					.Where( x =>
						_selectedKeys.Contains( x.StartKeyId )
						&& _selectedKeys.Contains( x.EndKeyId ) )
					.ToList()
			} );
		}

		foreach ( var track in clip.VisibilityTracks )
		{
			var keys = track.Keys.Where( x => _selectedKeys.Contains( x.Id ) ).ToList();
			if ( keys.Count == 0 )
				continue;

			payload.VisibilityTracks.Add( new ClipboardVisibilityTrack
			{
				PartId = track.PartId,
				Keys = keys
			} );
		}

		KeyClipboard = Json.Serialize( payload );
	}

	public void CutSelectedKeys()
	{
		CopySelectedKeys();
		DeleteSelectedKeys();
	}

	public void PasteKeys()
	{
		if ( string.IsNullOrWhiteSpace( KeyClipboard ) )
			return;

		var payload = Json.Deserialize<KeyClipboardPayload>( KeyClipboard );
		var clip = Document.GetSelectedClip();
		if ( payload is null || clip is null )
			return;

		var pasteTime = Document.Workspace.TimelineTime;
		Mutate( "Paste keys", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			_selectedKeys.Clear();
			foreach ( var sourceTrack in payload.Tracks )
			{
				var targetTrack = clip.EnsureTrack( sourceTrack.Target );
				targetTrack.Kind = sourceTrack.Kind;
				targetTrack.Interpolation = sourceTrack.Interpolation;
				var previousOrder = CurveEditingService.CaptureOrder( targetTrack );

				var remappedKeys = new Dictionary<Guid, Guid>();
				foreach ( var sourceKey in sourceTrack.Keys )
				{
					var key = Json.Deserialize<TransformKey>( Json.Serialize( sourceKey ) )!;
					var sourceId = key.Id;
					key.Id = Guid.NewGuid();
					remappedKeys[sourceId] = key.Id;
					key.Time = TimelineInteraction.SnapTime(
						clip,
						pasteTime + sourceKey.Time - payload.Origin );
					targetTrack.Keys.Add( key );
					_selectedKeys.Add( key.Id );
				}

				var pastedFrames = targetTrack.Keys
					.Where( x => _selectedKeys.Contains( x.Id ) )
					.Select( x => TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) )
					.ToHashSet();
				targetTrack.Keys.RemoveAll( x =>
					!_selectedKeys.Contains( x.Id )
					&& pastedFrames.Contains(
						TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) ) );

				foreach ( var sourceSpan in sourceTrack.CurveSpans )
				{
					if ( !remappedKeys.TryGetValue( sourceSpan.StartKeyId, out var startId )
						|| !remappedKeys.TryGetValue( sourceSpan.EndKeyId, out var endId ) )
						continue;
					var span = Json.Deserialize<TransformCurveSpan>(
						Json.Serialize( sourceSpan ) )!;
					span.Id = Guid.NewGuid();
					span.StartKeyId = startId;
					span.EndKeyId = endId;
					targetTrack.CurveSpans.Add( span );
				}
				targetTrack.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
				CurveEditingService.RepairTopology( targetTrack, previousOrder );
			}

			foreach ( var sourceTrack in payload.VisibilityTracks )
			{
				if ( Document.Rig.VisibilityParts.All( x => x.Id != sourceTrack.PartId ) )
					continue;
				var targetTrack = clip.EnsureVisibilityTrack( sourceTrack.PartId );
				foreach ( var sourceKey in sourceTrack.Keys )
				{
					var key = Json.Deserialize<VisibilityKey>( Json.Serialize( sourceKey ) )!;
					key.Id = Guid.NewGuid();
					key.Time = TimelineInteraction.SnapTime(
						clip,
						pasteTime + sourceKey.Time - payload.Origin );
					targetTrack.Keys.Add( key );
					_selectedKeys.Add( key.Id );
				}

				targetTrack.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
			}
		} );

		KeySelectionChanged?.Invoke();
	}

	public void DeleteSelectedKeys()
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null || _selectedKeys.Count == 0 )
			return;

		Mutate( "Delete keys", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			foreach ( var track in clip.Tracks )
			{
				CurveEditingService.RemoveKeysAndRepair(
					track,
					x => _selectedKeys.Contains( x.Id ) );
			}
			foreach ( var track in clip.VisibilityTracks )
				track.Keys.RemoveAll( x => _selectedKeys.Contains( x.Id ) );
			_selectedKeys.Clear();
		} );

		KeySelectionChanged?.Invoke();
	}

	public void ReverseKeys()
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;

		var allKeyIds = clip.Tracks
			.SelectMany( x => x.Keys.Select( key => key.Id ) )
			.Concat( clip.VisibilityTracks.SelectMany( x => x.Keys.Select( key => key.Id ) ) )
			.ToHashSet();
		var reverseSelection = _selectedKeys.Where( allKeyIds.Contains ).ToHashSet();
		var hasSelection = reverseSelection.Count > 0;
		if ( !hasSelection )
			reverseSelection = allKeyIds;
		if ( reverseSelection.Count == 0 )
			return;

		var selectedFrames = clip.Tracks
			.SelectMany( x => x.Keys )
			.Select( x => (x.Id, x.Time) )
			.Concat( clip.VisibilityTracks
				.SelectMany( x => x.Keys )
				.Select( x => (x.Id, x.Time) ) )
			.Where( x => reverseSelection.Contains( x.Id ) )
			.Select( x => TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) )
			.ToArray();
		var firstFrame = hasSelection ? selectedFrames.Min() : 0;
		var lastFrame = hasSelection
			? selectedFrames.Max()
			: TimelineInteraction.LastFrame( clip );
		if ( firstFrame == lastFrame )
			return;

		SetPlaying( false );
		Mutate( hasSelection ? "Reverse selected keys" : "Reverse clip keys", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			foreach ( var track in clip.Tracks )
				ReverseTransformTrack(
					clip,
					track,
					reverseSelection,
					firstFrame,
					lastFrame );
			foreach ( var track in clip.VisibilityTracks )
				ReverseVisibilityTrack(
					clip,
					track,
					reverseSelection,
					firstFrame,
					lastFrame );
		} );
	}

	// Keeps an already-open pre-update window safe during S&box hotload.
	[Obsolete( "Use ReverseKeys." )]
	public void MirrorSelectedKeys() => ReverseKeys();

	public void UpsertSelectedTransformKey( string target, RigControlKind kind, Transform value )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;

		var snapped = TimelineInteraction.SnapTime(
			clip,
			Document.Workspace.TimelineTime );

		Mutate( $"Key {target}", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			var track = clip.EnsureTrack( target );
			track.Kind = kind;
			var key = WeaponAnimationMath.UpsertKey( track, snapped, value );
			CurveEditingService.RepairTrack( track );
			Document.Workspace.RemoveWorkingPose( clip.Id, target );
			_selectedKeys.Clear();
			_selectedKeys.Add( key.Id );
			clip.Readiness = clip.Role == WeaponClipRole.Idle
				? ClipReadiness.Ready
				: ClipReadiness.Draft;
		} );

		KeySelectionChanged?.Invoke();
	}

	public void KeySelectedTransform()
	{
		var selectedControl = Document.Workspace.SelectedControl;
		if ( !string.IsNullOrWhiteSpace( selectedControl ) )
		{
			var target = selectedControl switch
			{
				"@primary_hand" => Document.Binding.PrimaryHand,
				"@support_hand" => Document.Binding.SupportHand,
				"@primary_elbow" => Document.Binding.PrimaryElbowPole,
				"@support_elbow" => Document.Binding.SupportElbowPole,
				_ => null
			};
			if ( target is null )
				return;

			var clip = Document.GetSelectedClip();
			var fallback = clip?.Tracks.FirstOrDefault( x =>
					x.Target.Equals( selectedControl, StringComparison.OrdinalIgnoreCase ) ) is { } track
				? WeaponAnimationMath.SampleTrack(
					track,
					Document.Workspace.TimelineTime,
					target.Transform )
				: target.Transform;
			CommitWorkingPose( selectedControl, RigControlKind.Arm, fallback );
			return;
		}

		var selectedBone = Document.Workspace.SelectedBone;
		if ( string.IsNullOrWhiteSpace( selectedBone ) )
			return;
		var weaponSelection = selectedBone.Equals(
				"weapon_root",
				StringComparison.OrdinalIgnoreCase )
			|| Document.Rig.RetainedBones().Any( definition =>
				definition.Name.Equals( selectedBone, StringComparison.OrdinalIgnoreCase ) );
		var skeleton = HostSkeletonBuilder.BuildCached(
			Document,
			includeArmProfile: !weaponSelection );
		if ( !skeleton.ByName.TryGetValue( selectedBone, out var bone ) )
			return;

		var selectedClip = Document.GetSelectedClip();
		var bind = skeleton.GetBindLocal( bone );
		var selectedTrack = selectedClip?.Tracks.FirstOrDefault( x =>
			x.Target.Equals( selectedBone, StringComparison.OrdinalIgnoreCase ) );
		var value = selectedTrack is null
			? bind
			: WeaponAnimationMath.SampleTrack(
				selectedTrack,
				Document.Workspace.TimelineTime,
				bind );
		CommitWorkingPose(
			selectedBone,
			bone.IsWeaponBone ? RigControlKind.Weapon : RigControlKind.Arm,
			value );
	}

	public void ApplyTransformEdit( string target, RigControlKind kind, Transform value )
	{
		if ( Document.Workspace.AutoKey )
		{
			UpsertSelectedTransformKey( target, kind, value );
			return;
		}

		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;
		Mutate(
			$"Pose {target}",
			document => document.Workspace.SetWorkingPose( clip.Id, target, kind, value ) );
	}

	public void UpdateTransformEditContinuous(
		string target,
		RigControlKind kind,
		Transform value )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;

		UpdateContinuousEdit( document =>
		{
			if ( !document.Workspace.AutoKey )
			{
				document.Workspace.SetWorkingPose( clip.Id, target, kind, value );
				return;
			}

			var snapped = TimelineInteraction.SnapTime(
				clip,
				document.Workspace.TimelineTime );
			IdleBindPoseService.MarkAuthored( clip );
			var track = clip.EnsureTrack( target );
			track.Kind = kind;
			WeaponAnimationMath.UpsertKey( track, snapped, value );
			CurveEditingService.RepairTrack( track );
			document.Workspace.RemoveWorkingPose( clip.Id, target );
			clip.Readiness = clip.Role == WeaponClipRole.Idle
				? ClipReadiness.Ready
				: ClipReadiness.Draft;
		} );
	}

	public void CommitWorkingPose(
		string target,
		RigControlKind kind,
		Transform fallback )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;
		var value = Document.Workspace.GetWorkingPose( clip.Id, target )?.Transform ?? fallback;
		UpsertSelectedTransformKey( target, kind, value );
	}

	public void DiscardWorkingPose( string target )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null || Document.Workspace.GetWorkingPose( clip.Id, target ) is null )
			return;
		Mutate(
			$"Revert {target}",
			document => document.Workspace.RemoveWorkingPose( clip.Id, target ) );
	}

	public bool HasKeyAtPlayhead( string target )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return false;
		var snapped = TimelineInteraction.SnapTime(
			clip,
			Document.Workspace.TimelineTime );
		return clip.Tracks
			.FirstOrDefault( x => x.Target.Equals( target, StringComparison.OrdinalIgnoreCase ) )?
			.Keys.Any( x => MathF.Abs( x.Time - snapped ) <= 0.0001f ) == true;
	}

	public WeaponVisibilityPart? GetVisibilityPart( string boneName ) =>
		Document.Rig.VisibilityParts.FirstOrDefault( x =>
			x.BoneName.Equals( boneName, StringComparison.OrdinalIgnoreCase ) );

	public void AddVisibilityPart( string boneName )
	{
		var definition = Document.Rig.RetainedBones().FirstOrDefault( x =>
			x.Name.Equals( boneName, StringComparison.OrdinalIgnoreCase ) );
		if ( definition is null || GetVisibilityPart( boneName ) is not null )
			return;

		Mutate( $"Enable visibility for {boneName}", document =>
		{
			document.Rig.VisibilityParts.Add( new WeaponVisibilityPart
			{
				Name = DisplayVisibilityName( boneName ),
				BoneId = definition.Id,
				BoneName = definition.Name
			} );
		} );
	}

	public void RemoveVisibilityPart( Guid partId )
	{
		var part = Document.Rig.VisibilityParts.FirstOrDefault( x => x.Id == partId );
		if ( part is null )
			return;

		Mutate( $"Remove visibility from {part.Name}", document =>
		{
			document.Rig.VisibilityParts.RemoveAll( x => x.Id == partId );
			foreach ( var clip in document.Clips )
				clip.VisibilityTracks.RemoveAll( x => x.PartId == partId );
			_selectedKeys.Clear();
		} );
		KeySelectionChanged?.Invoke();
	}

	public void UpdateVisibilityPart(
		Guid partId,
		string description,
		Action<WeaponVisibilityPart> mutation )
	{
		var part = Document.Rig.VisibilityParts.FirstOrDefault( x => x.Id == partId );
		if ( part is null )
			return;
		Mutate( description, _ => mutation( part ) );
	}

	public bool EvaluateVisibility( Guid partId )
	{
		var part = Document.Rig.VisibilityParts.FirstOrDefault( x => x.Id == partId );
		return part is not null && WeaponVisibilityEvaluator.Evaluate(
			part,
			Document.GetSelectedClip(),
			Document.Workspace.TimelineTime );
	}

	public bool HasVisibilityKeyAtPlayhead( Guid partId )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return false;
		var snapped = TimelineInteraction.SnapTime(
			clip,
			Document.Workspace.TimelineTime );
		return clip.VisibilityTracks.FirstOrDefault( x => x.PartId == partId )?
			.Keys.Any( x => MathF.Abs( x.Time - snapped ) <= 0.0001f ) == true;
	}

	public void UpsertVisibilityKey( Guid partId, bool visible )
	{
		var clip = Document.GetSelectedClip();
		var part = Document.Rig.VisibilityParts.FirstOrDefault( x => x.Id == partId );
		if ( clip is null || part is null )
			return;

		var snapped = TimelineInteraction.SnapTime(
			clip,
			Document.Workspace.TimelineTime );
		Mutate( $"{(visible ? "Show" : "Hide")} {part.Name}", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			var key = WeaponVisibilityEvaluator.UpsertKey(
				clip.EnsureVisibilityTrack( partId ),
				snapped,
				visible );
			_selectedKeys.Clear();
			_selectedKeys.Add( key.Id );
			clip.Readiness = clip.Role == WeaponClipRole.Idle
				? ClipReadiness.Ready
				: ClipReadiness.Draft;
		} );
		KeySelectionChanged?.Invoke();
	}

	public void RemoveVisibilityKeyAtPlayhead( Guid partId )
	{
		var clip = Document.GetSelectedClip();
		var part = Document.Rig.VisibilityParts.FirstOrDefault( x => x.Id == partId );
		if ( clip is null || part is null )
			return;
		var snapped = TimelineInteraction.SnapTime(
			clip,
			Document.Workspace.TimelineTime );
		Mutate( $"Remove {part.Name} visibility key", _ =>
		{
			var track = clip.VisibilityTracks.FirstOrDefault( x => x.PartId == partId );
			if ( track is null )
				return;
			track.Keys.RemoveAll( x => MathF.Abs( x.Time - snapped ) <= 0.0001f );
			if ( track.Keys.Count == 0 )
				clip.VisibilityTracks.Remove( track );
		} );
	}

	private static string DisplayVisibilityName( string boneName )
	{
		var words = boneName
			.Replace( '_', ' ' )
			.Replace( '-', ' ' )
			.Trim();
		return string.IsNullOrWhiteSpace( words )
			? "Visible Part"
			: string.Join( " ", words
				.Split( ' ', StringSplitOptions.RemoveEmptyEntries )
				.Select( x => char.ToUpperInvariant( x[0] ) + x[1..] ) );
	}

	private void SetTimelineFrameInternal(
		int frame,
		WeaponAnimationClip clip,
		bool syncPlaybackTime = true )
	{
		frame = Math.Clamp( frame, 0, TimelineInteraction.LastFrame( clip ) );
		var time = TimelineInteraction.FrameToTime( frame, clip.SampleRate );
		if ( syncPlaybackTime )
			_playbackTime = time;
		if ( MathF.Abs( Document.Workspace.TimelineTime - time ) <= 0.00001f )
			return;

		Document.Workspace.TimelineTime = time;
		TimelineChanged?.Invoke();
	}

	private void SetPlaying( bool value )
	{
		if ( _isPlaying == value )
			return;
		_isPlaying = value;
		PlaybackChanged?.Invoke();
	}

	private static void ApplySelectedKeyMove(
		WeaponAnimationClip clip,
		IReadOnlyDictionary<Guid, float> startTimes,
		int deltaFrames )
	{
		foreach ( var key in clip.Tracks.SelectMany( x => x.Keys ) )
		{
			if ( !startTimes.TryGetValue( key.Id, out var start ) )
				continue;
			var frame = TimelineInteraction.TimeToFrame( start, clip.SampleRate ) + deltaFrames;
			key.Time = TimelineInteraction.FrameToTime(
				Math.Clamp( frame, 0, TimelineInteraction.LastFrame( clip ) ),
				clip.SampleRate );
		}

		foreach ( var key in clip.VisibilityTracks.SelectMany( x => x.Keys ) )
		{
			if ( !startTimes.TryGetValue( key.Id, out var start ) )
				continue;
			var frame = TimelineInteraction.TimeToFrame( start, clip.SampleRate ) + deltaFrames;
			key.Time = TimelineInteraction.FrameToTime(
				Math.Clamp( frame, 0, TimelineInteraction.LastFrame( clip ) ),
				clip.SampleRate );
		}

		foreach ( var track in clip.Tracks )
		{
			track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
			CurveEditingService.RepairTrack( track );
		}
		foreach ( var track in clip.VisibilityTracks )
			track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
	}

	private static void ReverseTransformTrack(
		WeaponAnimationClip clip,
		TransformTrack track,
		IReadOnlySet<Guid> reversedKeys,
		int firstFrame,
		int lastFrame )
	{
		CurveEditingService.RepairTrack( track );
		var originalOrder = track.Keys
			.OrderBy( x => x.Time )
			.Select( x => x.Id )
			.ToArray();
		var originalAdjacent = originalOrder
			.Zip( originalOrder.Skip( 1 ), ( start, end ) => (start, end) )
			.ToHashSet();
		var originalSpans = track.CurveSpans
			.GroupBy( x => (x.StartKeyId, x.EndKeyId) )
			.ToDictionary( x => x.Key, x => CloneCurveSpan( x.First() ) );
		var originalTangents = track.Keys.ToDictionary(
			x => x.Id,
			x => CloneCurveTangents( x.CurveTangents ) );
		var originalLegacyTangents = track.Keys.ToDictionary(
			x => x.Id,
			x => (x.InTangent, x.OutTangent) );

		foreach ( var key in track.Keys.Where( x => reversedKeys.Contains( x.Id ) ) )
		{
			var frame = TimelineInteraction.TimeToFrame( key.Time, clip.SampleRate );
			key.Time = TimelineInteraction.FrameToTime(
				firstFrame + lastFrame - frame,
				clip.SampleRate );
		}

		RemoveUnselectedCollisions( clip, track.Keys, reversedKeys );
		track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
		track.CurveSpans.Clear();
		foreach ( var (start, end) in track.Keys.Zip(
			track.Keys.Skip( 1 ),
			( start, end ) => (start, end) ) )
		{
			if ( originalSpans.TryGetValue( (start.Id, end.Id), out var forwardSpan ) )
			{
				track.CurveSpans.Add( CloneCurveSpan( forwardSpan ) );
				continue;
			}

			if ( originalSpans.TryGetValue( (end.Id, start.Id), out var backwardSpan ) )
			{
				track.CurveSpans.Add( ReverseCurveSpan( backwardSpan ) );
				ApplyReversedTangents(
					start,
					end,
					originalTangents,
					originalLegacyTangents );
				continue;
			}

			if ( originalAdjacent.Contains( (end.Id, start.Id) ) )
			{
				ApplyReversedTangents(
					start,
					end,
					originalTangents,
					originalLegacyTangents );
				continue;
			}

			if ( originalAdjacent.Contains( (start.Id, end.Id) ) )
				continue;

			// New neighbors exposed by a collision use an explicit, predictable bridge.
			var bridge = track.EnsureCurveSpan( start.Id, end.Id );
			bridge.HasInterpolationOverride = true;
			bridge.Interpolation = TrackInterpolation.Linear;
		}

		CurveEditingService.RepairTrack( track );
	}

	private static void ReverseVisibilityTrack(
		WeaponAnimationClip clip,
		VisibilityTrack track,
		IReadOnlySet<Guid> reversedKeys,
		int firstFrame,
		int lastFrame )
	{
		foreach ( var key in track.Keys.Where( x => reversedKeys.Contains( x.Id ) ) )
		{
			var frame = TimelineInteraction.TimeToFrame( key.Time, clip.SampleRate );
			key.Time = TimelineInteraction.FrameToTime(
				firstFrame + lastFrame - frame,
				clip.SampleRate );
		}

		RemoveUnselectedCollisions( clip, track.Keys, reversedKeys );
		track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
	}

	private static void RemoveUnselectedCollisions(
		WeaponAnimationClip clip,
		List<TransformKey> keys,
		IReadOnlySet<Guid> reversedKeys )
	{
		var reversedFrames = keys
			.Where( x => reversedKeys.Contains( x.Id ) )
			.Select( x => TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) )
			.ToHashSet();
		keys.RemoveAll( x =>
			!reversedKeys.Contains( x.Id )
			&& reversedFrames.Contains(
				TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) ) );
	}

	private static void RemoveUnselectedCollisions(
		WeaponAnimationClip clip,
		List<VisibilityKey> keys,
		IReadOnlySet<Guid> reversedKeys )
	{
		var reversedFrames = keys
			.Where( x => reversedKeys.Contains( x.Id ) )
			.Select( x => TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) )
			.ToHashSet();
		keys.RemoveAll( x =>
			!reversedKeys.Contains( x.Id )
			&& reversedFrames.Contains(
				TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) ) );
	}

	private static TransformCurveSpan CloneCurveSpan( TransformCurveSpan source ) => new()
	{
		Id = source.Id,
		StartKeyId = source.StartKeyId,
		EndKeyId = source.EndKeyId,
		HasSpeedCurve = source.HasSpeedCurve,
		Speed = CloneMotionRateCurve( source.Speed ),
		HasInterpolationOverride = source.HasInterpolationOverride,
		Interpolation = source.Interpolation,
		CustomChannels = source.CustomChannels
	};

	private static TransformCurveSpan ReverseCurveSpan( TransformCurveSpan source )
	{
		var result = CloneCurveSpan( source );
		result.StartKeyId = source.EndKeyId;
		result.EndKeyId = source.StartKeyId;
		result.Speed = new MotionRateCurve
		{
			StartRate = source.Speed.EndRate,
			EndRate = source.Speed.StartRate,
			StartSlope = -source.Speed.EndSlope,
			EndSlope = -source.Speed.StartSlope,
			StartHandleMode = source.Speed.EndHandleMode,
			EndHandleMode = source.Speed.StartHandleMode
		};
		return result;
	}

	private static MotionRateCurve CloneMotionRateCurve( MotionRateCurve source ) => new()
	{
		StartRate = source.StartRate,
		EndRate = source.EndRate,
		StartSlope = source.StartSlope,
		EndSlope = source.EndSlope,
		StartHandleMode = source.StartHandleMode,
		EndHandleMode = source.EndHandleMode
	};

	private static TransformCurveTangents CloneCurveTangents(
		TransformCurveTangents source ) => new()
	{
		PositionIn = source.PositionIn,
		PositionOut = source.PositionOut,
		RotationIn = source.RotationIn,
		RotationOut = source.RotationOut,
		ScaleIn = source.ScaleIn,
		ScaleOut = source.ScaleOut,
		FreeHandles = source.FreeHandles
	};

	private static void ApplyReversedTangents(
		TransformKey start,
		TransformKey end,
		IReadOnlyDictionary<Guid, TransformCurveTangents> tangents,
		IReadOnlyDictionary<Guid, (Vector3 In, Vector3 Out)> legacyTangents )
	{
		var startSource = tangents[start.Id];
		var endSource = tangents[end.Id];
		start.CurveTangents.PositionOut = -startSource.PositionIn;
		start.CurveTangents.RotationOut = -startSource.RotationIn;
		start.CurveTangents.ScaleOut = -startSource.ScaleIn;
		end.CurveTangents.PositionIn = -endSource.PositionOut;
		end.CurveTangents.RotationIn = -endSource.RotationOut;
		end.CurveTangents.ScaleIn = -endSource.ScaleOut;

		start.OutTangent = -legacyTangents[start.Id].In;
		end.InTangent = -legacyTangents[end.Id].Out;
	}

	private void RemoveKeyCollisions( WeaponAnimationClip clip )
	{
		foreach ( var track in clip.Tracks )
		{
			var previousOrder = CurveEditingService.CaptureOrder( track );
			var occupied = track.Keys
				.Where( x => _selectedKeys.Contains( x.Id ) )
				.Select( x => TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) )
				.ToHashSet();
			track.Keys.RemoveAll( x =>
				!_selectedKeys.Contains( x.Id )
				&& occupied.Contains(
					TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) ) );
			CurveEditingService.RepairTopology( track, previousOrder );
		}

		foreach ( var track in clip.VisibilityTracks )
		{
			var occupied = track.Keys
				.Where( x => _selectedKeys.Contains( x.Id ) )
				.Select( x => TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) )
				.ToHashSet();
			track.Keys.RemoveAll( x =>
				!_selectedKeys.Contains( x.Id )
				&& occupied.Contains(
					TimelineInteraction.TimeToFrame( x.Time, clip.SampleRate ) ) );
		}
	}

	private void SetDirty( bool value )
	{
		if ( IsDirty == value )
			return;

		IsDirty = value;
		DirtyChanged?.Invoke();
	}

	private static string Serialize( WeaponAnimationDocument document ) => Json.Serialize( document );
	private static WeaponAnimationDocument Deserialize( string json ) =>
		Json.Deserialize<WeaponAnimationDocument>( json ) ?? WeaponAnimationDocument.CreateDefault();

	private readonly record struct DocumentSnapshot( string Description, string Json );

	private sealed class KeyClipboardPayload
	{
		public float Origin { get; set; }
		public List<ClipboardTrack> Tracks { get; set; } = [];
		public List<ClipboardVisibilityTrack> VisibilityTracks { get; set; } = [];
	}

	private sealed class ClipboardTrack
	{
		public string Target { get; set; } = "";
		public RigControlKind Kind { get; set; }
		public TrackInterpolation Interpolation { get; set; }
		public List<TransformKey> Keys { get; set; } = [];
		public List<TransformCurveSpan> CurveSpans { get; set; } = [];
	}

	private sealed class ClipboardVisibilityTrack
	{
		public Guid PartId { get; set; }
		public List<VisibilityKey> Keys { get; set; } = [];
	}
}
