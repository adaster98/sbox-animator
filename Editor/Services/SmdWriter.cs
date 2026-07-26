#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class SmdWriter
{
	private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

	public static string WriteReference( HostSkeleton skeleton )
	{
		var builder = Begin( skeleton );
		builder.AppendLine( "skeleton" );
		builder.AppendLine( "time 0" );
		foreach ( var bone in skeleton.Bones )
			AppendBoneTransform( builder, bone.Index, skeleton.GetBindLocal( bone ) );
		builder.AppendLine( "end" );

		// ModelDoc requires at least one weighted render triangle.
		builder.AppendLine( "triangles" );
		builder.AppendLine( "materials/dev/gray_25.vmat" );
		AppendVertex( builder, 0, new Vector3( 0, 0, 0 ), new Vector2( 0, 0 ) );
		AppendVertex( builder, 0, new Vector3( 0.01f, 0, 0 ), new Vector2( 1, 0 ) );
		AppendVertex( builder, 0, new Vector3( 0, 0.01f, 0 ), new Vector2( 0, 1 ) );
		builder.AppendLine( "end" );
		return builder.ToString();
	}

	public static string WriteClip(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		WeaponAnimationClip clip )
	{
		var builder = Begin( skeleton );
		builder.AppendLine( "skeleton" );
		var frameCount = Math.Max( 1, (int)MathF.Round( clip.Duration * clip.SampleRate ) );

		for ( var frame = 0; frame <= frameCount; frame++ )
		{
			var time = MathF.Min( frame / clip.SampleRate, clip.Duration );
			var pose = AnimationPoseEvaluator.Evaluate( document, skeleton, clip, time );
			builder.Append( "time " ).AppendLine( frame.ToString( Invariant ) );
			foreach ( var bone in skeleton.Bones )
				AppendBoneTransform( builder, bone.Index, pose.Local[bone.Name] );
		}

		builder.AppendLine( "end" );
		return builder.ToString();
	}

	private static StringBuilder Begin( HostSkeleton skeleton )
	{
		var builder = new StringBuilder();
		builder.AppendLine( "version 1" );
		builder.AppendLine( "nodes" );
		foreach ( var bone in skeleton.Bones )
		{
			var parentIndex = string.IsNullOrWhiteSpace( bone.ParentName )
				? -1
				: skeleton.ByName.GetValueOrDefault( bone.ParentName )?.Index ?? -1;
			builder.Append( bone.Index )
				.Append( " \"" )
				.Append( Escape( bone.Name ) )
				.Append( "\" " )
				.AppendLine( parentIndex.ToString( Invariant ) );
		}
		builder.AppendLine( "end" );
		return builder;
	}

	private static void AppendBoneTransform( StringBuilder builder, int index, Transform transform )
	{
		var euler = QuaternionToEulerRadians( transform.Rotation.Normal );
		builder.Append( index ).Append( ' ' )
			.Append( F( transform.Position.x ) ).Append( ' ' )
			.Append( F( transform.Position.y ) ).Append( ' ' )
			.Append( F( transform.Position.z ) ).Append( ' ' )
			.Append( F( euler.x ) ).Append( ' ' )
			.Append( F( euler.y ) ).Append( ' ' )
			.AppendLine( F( euler.z ) );
	}

	private static void AppendVertex(
		StringBuilder builder,
		int bone,
		Vector3 position,
		Vector2 uv )
	{
		builder.Append( bone ).Append( ' ' )
			.Append( F( position.x ) ).Append( ' ' )
			.Append( F( position.y ) ).Append( ' ' )
			.Append( F( position.z ) )
			.Append( " 0 0 1 " )
			.Append( F( uv.x ) ).Append( ' ' )
			.Append( F( uv.y ) )
			.Append( " 1 " ).Append( bone ).AppendLine( " 1" );
	}

	private static Vector3 QuaternionToEulerRadians( Rotation rotation )
	{
		var x = rotation.x;
		var y = rotation.y;
		var z = rotation.z;
		var w = rotation.w;

		var sinRoll = 2.0f * (w * x + y * z);
		var cosRoll = 1.0f - 2.0f * (x * x + y * y);
		var roll = MathF.Atan2( sinRoll, cosRoll );

		var sinPitch = 2.0f * (w * y - z * x);
		var pitch = MathF.Abs( sinPitch ) >= 1.0f
			? MathF.CopySign( MathF.PI / 2.0f, sinPitch )
			: MathF.Asin( sinPitch );

		var sinYaw = 2.0f * (w * z + x * y);
		var cosYaw = 1.0f - 2.0f * (y * y + z * z);
		var yaw = MathF.Atan2( sinYaw, cosYaw );
		return new Vector3( roll, pitch, yaw );
	}

	private static string F( float value ) => value.ToString( "0.######", Invariant );
	private static string Escape( string value ) => value.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
}
