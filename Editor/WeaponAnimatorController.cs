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

	public WeaponAnimationDocument Document { get; private set; } = WeaponAnimationDocument.CreateDefault();
	public bool IsDirty { get; private set; }
	public string LastAction { get; private set; } = "";
	public IReadOnlyCollection<Guid> SelectedKeys => _selectedKeys;

	private readonly HashSet<Guid> _selectedKeys = [];

	public event Action? DocumentChanged;
	public event Action? PoseChanged;
	public event Action? SelectionChanged;
	public event Action? DirtyChanged;
	public event Action? TimelineChanged;

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
		IsDirty = false;
		LastAction = "";
		DocumentChanged?.Invoke();
		SelectionChanged?.Invoke();
		DirtyChanged?.Invoke();
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
	}

	public void Undo()
	{
		EndContinuousEdit();
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
	}

	public void Redo()
	{
		EndContinuousEdit();
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
		SelectionChanged?.Invoke();
		DocumentChanged?.Invoke();
	}

	public void SetTimelineTime( float time )
	{
		var clip = Document.GetSelectedClip();
		var maximum = clip?.Duration ?? 0;
		var clamped = Math.Clamp( time, 0, maximum );
		if ( MathF.Abs( Document.Workspace.TimelineTime - clamped ) <= 0.00001f )
			return;

		Document.Workspace.TimelineTime = clamped;
		TimelineChanged?.Invoke();
	}

	public void SelectKeys( IEnumerable<Guid> keyIds, bool additive )
	{
		if ( !additive )
			_selectedKeys.Clear();

		foreach ( var key in keyIds )
			_selectedKeys.Add( key );
		SelectionChanged?.Invoke();
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
				Keys = keys
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

				foreach ( var sourceKey in sourceTrack.Keys )
				{
					var key = Json.Deserialize<TransformKey>( Json.Serialize( sourceKey ) )!;
					key.Id = Guid.NewGuid();
					key.Time = WeaponAnimationMath.SnapTime(
						pasteTime + sourceKey.Time - payload.Origin,
						clip.SampleRate,
						clip.AllowSubframeKeys );
					targetTrack.Keys.Add( key );
					_selectedKeys.Add( key.Id );
				}

				targetTrack.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
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
					key.Time = WeaponAnimationMath.SnapTime(
						pasteTime + sourceKey.Time - payload.Origin,
						clip.SampleRate,
						clip.AllowSubframeKeys );
					targetTrack.Keys.Add( key );
					_selectedKeys.Add( key.Id );
				}

				targetTrack.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
			}
		} );

		SelectionChanged?.Invoke();
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
				track.Keys.RemoveAll( x => _selectedKeys.Contains( x.Id ) );
			foreach ( var track in clip.VisibilityTracks )
				track.Keys.RemoveAll( x => _selectedKeys.Contains( x.Id ) );
			_selectedKeys.Clear();
		} );

		SelectionChanged?.Invoke();
	}

	public void MirrorSelectedKeys()
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null || _selectedKeys.Count == 0 )
			return;

		Mutate( "Mirror keys", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			foreach ( var key in clip.Tracks
				.SelectMany( x => x.Keys )
				.Where( x => _selectedKeys.Contains( x.Id ) ) )
			{
				key.Position = key.Position.WithY( -key.Position.y );
				var angles = key.Rotation.Angles();
				key.Rotation = Rotation.From( angles.WithPitch( -angles.pitch ).WithRoll( -angles.roll ) );
			}
		} );
	}

	public void UpsertSelectedTransformKey( string target, RigControlKind kind, Transform value )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return;

		var snapped = WeaponAnimationMath.SnapTime(
			Document.Workspace.TimelineTime,
			clip.SampleRate,
			clip.AllowSubframeKeys );

		Mutate( $"Key {target}", _ =>
		{
			IdleBindPoseService.MarkAuthored( clip );
			var track = clip.EnsureTrack( target );
			track.Kind = kind;
			var key = WeaponAnimationMath.UpsertKey( track, snapped, value );
			Document.Workspace.RemoveWorkingPose( clip.Id, target );
			_selectedKeys.Clear();
			_selectedKeys.Add( key.Id );
			clip.Readiness = clip.Role == WeaponClipRole.Idle
				? ClipReadiness.Ready
				: ClipReadiness.Draft;
		} );

		SelectionChanged?.Invoke();
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
		SelectionChanged?.Invoke();
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

			var snapped = WeaponAnimationMath.SnapTime(
				document.Workspace.TimelineTime,
				clip.SampleRate,
				clip.AllowSubframeKeys );
			IdleBindPoseService.MarkAuthored( clip );
			var track = clip.EnsureTrack( target );
			track.Kind = kind;
			WeaponAnimationMath.UpsertKey( track, snapped, value );
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
		SelectionChanged?.Invoke();
	}

	public bool HasKeyAtPlayhead( string target )
	{
		var clip = Document.GetSelectedClip();
		if ( clip is null )
			return false;
		var snapped = WeaponAnimationMath.SnapTime(
			Document.Workspace.TimelineTime,
			clip.SampleRate,
			clip.AllowSubframeKeys );
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
		SelectionChanged?.Invoke();
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
		var snapped = WeaponAnimationMath.SnapTime(
			Document.Workspace.TimelineTime,
			clip.SampleRate,
			clip.AllowSubframeKeys );
		return clip.VisibilityTracks.FirstOrDefault( x => x.PartId == partId )?
			.Keys.Any( x => MathF.Abs( x.Time - snapped ) <= 0.0001f ) == true;
	}

	public void UpsertVisibilityKey( Guid partId, bool visible )
	{
		var clip = Document.GetSelectedClip();
		var part = Document.Rig.VisibilityParts.FirstOrDefault( x => x.Id == partId );
		if ( clip is null || part is null )
			return;

		var snapped = WeaponAnimationMath.SnapTime(
			Document.Workspace.TimelineTime,
			clip.SampleRate,
			clip.AllowSubframeKeys );
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
		SelectionChanged?.Invoke();
	}

	public void RemoveVisibilityKeyAtPlayhead( Guid partId )
	{
		var clip = Document.GetSelectedClip();
		var part = Document.Rig.VisibilityParts.FirstOrDefault( x => x.Id == partId );
		if ( clip is null || part is null )
			return;
		var snapped = WeaponAnimationMath.SnapTime(
			Document.Workspace.TimelineTime,
			clip.SampleRate,
			clip.AllowSubframeKeys );
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
	}

	private sealed class ClipboardVisibilityTrack
	{
		public Guid PartId { get; set; }
		public List<VisibilityKey> Keys { get; set; } = [];
	}
}
