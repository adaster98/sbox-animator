#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class SequenceImportResult
{
	public bool Success { get; init; }
	public string Message { get; init; } = "";
	public int ImportedTracks { get; init; }
}

public static class SequenceImportService
{
	public static IReadOnlyList<string> GetSequences( WeaponAnimationDocument document )
	{
		if ( string.IsNullOrWhiteSpace( document.Source.CompiledModelPath ) )
			return [];

		using var preview = new SequenceSamplingScene( document.Source.CompiledModelPath );
		return preview.IsValid
			? preview.Renderer.Sequence.SequenceNames.ToArray()
			: [];
	}

	public static SequenceImportResult Import(
		WeaponAnimationDocument document,
		WeaponAnimationClip clip,
		string sequenceName )
	{
		using var preview = new SequenceSamplingScene( document.Source.CompiledModelPath );
		if ( !preview.IsValid )
			return Failed( "The source model could not be loaded for sequence sampling." );

		if ( !preview.Renderer.Sequence.SequenceNames.Contains( sequenceName ) )
			return Failed( $"Sequence '{sequenceName}' does not exist on the source model." );

		preview.Renderer.Sequence.Name = sequenceName;
		preview.Renderer.Sequence.Looping = false;
		preview.Renderer.Sequence.PlaybackRate = 0;
		preview.Tick();
		var duration = MathF.Max( preview.Renderer.Sequence.Duration, 1.0f / clip.SampleRate );
		var frames = Math.Max( 1, (int)MathF.Ceiling( duration * clip.SampleRate ) );
		var tracks = new Dictionary<string, TransformTrack>( StringComparer.OrdinalIgnoreCase );

		foreach ( var definition in document.Rig.RetainedBones() )
		{
			var target = definition.Id.Equals(
				document.Rig.SourceSkeletonRootId,
				StringComparison.OrdinalIgnoreCase )
				? "weapon_root"
				: definition.Name;
			tracks[definition.Name] = new TransformTrack
			{
				Target = target,
				Kind = RigControlKind.Weapon,
				Interpolation = TrackInterpolation.Cubic
			};
		}

		for ( var frame = 0; frame <= frames; frame++ )
		{
			var time = MathF.Min( frame / clip.SampleRate, duration );
			preview.Renderer.Sequence.Time = time;
			preview.Tick();

			foreach ( var definition in document.Rig.Bones )
			{
				if ( !tracks.TryGetValue( definition.Name, out var track ) )
					continue;
				var bone = preview.Renderer.Model.Bones.GetBone( definition.Name );
				if ( bone is null || !preview.Renderer.TryGetBoneTransformLocal( bone, out var model ) )
					continue;

				var local = model;
				if ( bone.Parent is not null
					&& preview.Renderer.TryGetBoneTransformLocal( bone.Parent, out var parent ) )
					local = parent.ToLocal( model );

				if ( definition.Id.Equals(
					document.Rig.SourceSkeletonRootId,
					StringComparison.OrdinalIgnoreCase ) )
				{
					var placement = WeaponAnimationMath.Compose(
						document.Calibration.PhysicalTransform,
						document.Calibration.FramingTransform );
					local = new Transform(
						placement.PointToWorld( local.Position ),
						placement.Rotation * local.Rotation,
						placement.Scale * local.Scale );
				}

				WeaponAnimationMath.UpsertKey( track, time, local );
			}
		}

		clip.Tracks.RemoveAll( x => x.Kind == RigControlKind.Weapon );
		clip.Tracks.AddRange( tracks.Values.Where( x => x.Keys.Count > 0 ) );
		clip.Duration = duration;
		clip.ImportedSequence = sequenceName;
		clip.Readiness = clip.Role == WeaponClipRole.Idle
			? ClipReadiness.Ready
			: ClipReadiness.Draft;

		return new SequenceImportResult
		{
			Success = true,
			Message = $"Imported '{sequenceName}' into {tracks.Count} editable tracks.",
			ImportedTracks = tracks.Count
		};
	}

	private static SequenceImportResult Failed( string message ) => new()
	{
		Success = false,
		Message = message
	};

	private sealed class SequenceSamplingScene : IDisposable
	{
		public Scene Scene { get; }
		public SkinnedModelRenderer Renderer { get; }
		public bool IsValid => Scene.IsValid() && Renderer.IsValid()
			&& Renderer.Model is not null && !Renderer.Model.IsError;

		public SequenceSamplingScene( string modelPath )
		{
			Scene = Scene.CreateEditorScene();
			using ( Scene.Push() )
			{
				Renderer = new GameObject( true, "sequence_sampler" )
					.GetOrAddComponent<SkinnedModelRenderer>( false );
				Renderer.Model = Model.Load( modelPath );
				Renderer.UseAnimGraph = false;
				Renderer.Enabled = true;
			}
			Tick();
		}

		public void Tick()
		{
			if ( Scene.IsValid() )
				Scene.EditorTick( RealTime.Now, 0 );
		}

		public void Dispose()
		{
			Scene?.Destroy();
		}
	}
}
