#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace SboxWeaponAnimator;

public readonly record struct WeaponVisibilitySpan(
	string Name,
	float StartTime,
	float EndTime,
	bool Visible );

public static class WeaponVisibilityEvaluator
{
	private const float TimeTolerance = 0.0001f;

	public static bool Evaluate(
		WeaponVisibilityPart part,
		WeaponAnimationClip? clip,
		float time )
	{
		var track = clip?.VisibilityTracks.FirstOrDefault( x =>
			x.PartId == part.Id && !x.Muted );
		if ( track is null )
			return part.DefaultVisible;

		var result = part.DefaultVisible;
		foreach ( var key in track.Keys )
		{
			if ( key.Time > time + TimeTolerance )
				break;
			result = key.Visible;
		}
		return result;
	}

	public static VisibilityKey UpsertKey(
		VisibilityTrack track,
		float time,
		bool visible )
	{
		var key = track.Keys.FirstOrDefault( x =>
			MathF.Abs( x.Time - time ) <= TimeTolerance );
		if ( key is null )
		{
			key = new VisibilityKey { Time = time };
			track.Keys.Add( key );
		}

		key.Visible = visible;
		track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
		return key;
	}

	public static string VisibleTag( Guid partId ) =>
		$"wepanim_part_{partId:N}_visible";

	public static string HiddenTag( Guid partId ) =>
		$"wepanim_part_{partId:N}_hidden";

	public static IReadOnlyList<WeaponVisibilitySpan> BuildSpans(
		WeaponVisibilityPart part,
		WeaponAnimationClip clip )
	{
		var duration = MathF.Max( clip.Duration, TimeTolerance );
		var transitions = clip.VisibilityTracks
			.FirstOrDefault( x => x.PartId == part.Id && !x.Muted )?
			.Keys
			.Where( x => x.Time >= 0 && x.Time <= duration + TimeTolerance )
			.OrderBy( x => x.Time )
			.ToArray() ?? [];
		var result = new List<WeaponVisibilitySpan>();
		var state = part.DefaultVisible;
		var start = 0.0f;

		foreach ( var key in transitions )
		{
			var time = Math.Clamp( key.Time, 0, duration );
			if ( key.Visible == state )
				continue;

			if ( time > start + TimeTolerance )
				result.Add( Span( part, start, time, state ) );
			state = key.Visible;
			start = time;
		}

		if ( start < duration - TimeTolerance )
			result.Add( Span( part, start, duration, state ) );
		else if ( result.Count == 0 )
			result.Add( Span( part, 0, duration, state ) );

		return result;
	}

	private static WeaponVisibilitySpan Span(
		WeaponVisibilityPart part,
		float start,
		float end,
		bool visible ) => new(
			visible ? VisibleTag( part.Id ) : HiddenTag( part.Id ),
			start,
			end,
			visible );
}
