#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class DmxWriter
{
	private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
	private const string CarrierMaterial = "materials/tools/toolsinvisible.vmat";
	private static readonly Vector3 BoneVisibilitySinkOffset = new( 0, 0, -8192 );

	public static string WriteAnimation(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		WeaponAnimationClip clip,
		CancellationToken cancellationToken = default )
	{
		cancellationToken.ThrowIfCancellationRequested();
		if ( skeleton.Bones.Count == 0 )
			throw new InvalidOperationException( "The animation host skeleton contains no bones." );

		var sampleRate = MathF.Max( clip.SampleRate, 1.0f );
		var frameCount = Math.Max( 1, (int)MathF.Round( clip.Duration * sampleRate ) );
		var compilerBindLocal = skeleton.BuildCompilerBindLocalTransforms();
		var times = new string[frameCount + 1];
		var poses = new IReadOnlyDictionary<string, Transform>[frameCount + 1];
		for ( var frame = 0; frame <= frameCount; frame++ )
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ( (frame & 7) == 7 )
				Thread.Yield();
			var time = MathF.Min( frame / sampleRate, clip.Duration );
			times[frame] = F( time );
			var evaluated = AnimationPoseEvaluator.Evaluate( document, skeleton, clip, time );
			ApplyBoneVisibility( document, skeleton, clip, time, evaluated );
			poses[frame] = BuildCompilerPoseLocals( skeleton, evaluated.Local );
		}

		var prefix = $"animation:{clip.Id}";
		var rootId = Id( $"{prefix}:root" );
		var modelId = Id( $"{prefix}:model" );
		var modelTransformId = Id( $"{prefix}:model-transform" );
		var baseStateId = Id( $"{prefix}:base-state" );
		var baseModelTransformId = Id( $"{prefix}:base-model-transform" );
		var animationListId = Id( $"{prefix}:animation-list" );
		var clipId = Id( $"{prefix}:clip" );
		var timeFrameId = Id( $"{prefix}:time-frame" );
		var builder = new StringBuilder();

		builder.AppendLine( "<!-- dmx encoding keyvalues2 4 format model 22 -->" );
		builder.AppendLine( "\"DmElement\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", rootId );
		Attribute( builder, 1, "name", "string", "root" );
		Attribute( builder, 1, "skeleton", "element", modelId );
		Attribute( builder, 1, "animationList", "element", animationListId );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeModel\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", modelId );
		Attribute( builder, 1, "name", "string", "weapon_animation_host" );
		Attribute( builder, 1, "transform", "element", modelTransformId );
		Attribute( builder, 1, "shape", "element", "" );
		Attribute( builder, 1, "visible", "bool", "1" );
		ElementArray(
			builder,
			1,
			"children",
			skeleton.Bones
				.Where( bone => string.IsNullOrWhiteSpace( bone.ParentName )
					|| !skeleton.ByName.ContainsKey( bone.ParentName ) )
				.Select( bone => AnimationJointId( prefix, bone.Index ) ) );
		ElementArray(
			builder,
			1,
			"jointList",
			new[] { modelId }.Concat(
				skeleton.Bones.Select( bone => AnimationJointId( prefix, bone.Index ) ) ) );
		ElementArray( builder, 1, "baseStates", [baseStateId] );
		Attribute( builder, 1, "upAxis", "string", "Z" );
		builder.AppendLine( "\t\"axisSystem\" \"DmeAxisSystem\"" );
		builder.AppendLine( "\t{" );
		Attribute( builder, 2, "id", "elementid", Id( $"{prefix}:axis-system" ) );
		Attribute( builder, 2, "name", "string", "" );
		Attribute( builder, 2, "upAxis", "int", "3" );
		Attribute( builder, 2, "forwardParity", "int", "1" );
		Attribute( builder, 2, "coordSys", "int", "0" );
		builder.AppendLine( "\t}" );
		builder.AppendLine( "}" );
		builder.AppendLine();

		foreach ( var bone in skeleton.Bones )
			WriteAnimationJoint( builder, skeleton, bone, prefix );

		ExternalTransformElement( builder, modelTransformId, "model", Transform.Zero );
		builder.AppendLine();

		foreach ( var bone in skeleton.Bones )
		{
			ExternalTransformElement(
				builder,
				AnimationTransformId( prefix, bone.Index ),
				bone.Name,
				compilerBindLocal[bone.Name] );
			builder.AppendLine();
		}

		ExternalTransformElement( builder, baseModelTransformId, "model", Transform.Zero );
		builder.AppendLine();
		foreach ( var bone in skeleton.Bones )
		{
			ExternalTransformElement(
				builder,
				AnimationBaseTransformId( prefix, bone.Index ),
				bone.Name,
				compilerBindLocal[bone.Name] );
			builder.AppendLine();
		}

		builder.AppendLine( "\"DmeTransformList\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", baseStateId );
		Attribute( builder, 1, "name", "string", "base" );
		ElementArray(
			builder,
			1,
			"transforms",
			new[] { baseModelTransformId }.Concat(
				skeleton.Bones.Select( bone =>
					AnimationBaseTransformId( prefix, bone.Index ) ) ) );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeAnimationList\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", animationListId );
		Attribute( builder, 1, "name", "string", clip.Name );
		ElementArray( builder, 1, "animations", [clipId] );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeChannelsClip\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", clipId );
		Attribute( builder, 1, "name", "string", WeaponAnimationNames.SequenceName( clip ) );
		Attribute( builder, 1, "timeFrame", "element", timeFrameId );
		Attribute( builder, 1, "color", "color", "0 0 0 0" );
		Attribute( builder, 1, "text", "string", "" );
		Attribute( builder, 1, "mute", "bool", "0" );
		ElementArray( builder, 1, "trackGroups", [] );
		Attribute( builder, 1, "displayScale", "float", "1" );
		ElementArray(
			builder,
			1,
			"channels",
			skeleton.Bones.SelectMany( bone => new[]
			{
				AnimationChannelId( prefix, bone.Index, "position" ),
				AnimationChannelId( prefix, bone.Index, "orientation" ),
				AnimationChannelId( prefix, bone.Index, "scale" )
			} ) );
		Attribute( builder, 1, "frameRate", "float", F( sampleRate ) );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeTimeFrame\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", timeFrameId );
		Attribute( builder, 1, "name", "string", "timeFrame" );
		Attribute( builder, 1, "start", "time", "0" );
		Attribute( builder, 1, "duration", "time", F( clip.Duration ) );
		Attribute( builder, 1, "offset", "time", "0" );
		Attribute( builder, 1, "scale", "float", "1" );
		builder.AppendLine( "}" );
		builder.AppendLine();

		foreach ( var bone in skeleton.Bones )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var values = poses
				.Select( pose => pose[bone.Name] )
				.ToArray();
			WriteVectorChannel(
				builder,
				prefix,
				bone,
				"position",
				times,
				values.Select( value => Vector( value.Position ) ).ToArray() );
			WriteQuaternionChannel(
				builder,
				prefix,
				bone,
				times,
				values.Select( value => Quaternion( value.Rotation.Normal ) ).ToArray() );
			WriteFloatChannel(
				builder,
				prefix,
				bone,
				times,
				values.Select( value => F( value.Scale.x ) ).ToArray() );
		}

		return builder.ToString();
	}

	internal static IReadOnlyDictionary<string, Transform> BuildCompilerPoseLocals(
		HostSkeleton skeleton,
		IReadOnlyDictionary<string, Transform> authoredLocal )
	{
		var authoredModel = BuildModelTransforms( skeleton, authoredLocal );
		var exportLocal = new Dictionary<string, Transform>( StringComparer.OrdinalIgnoreCase );
		var exportModel = new Dictionary<string, Transform>( StringComparer.OrdinalIgnoreCase );
		var pending = skeleton.Bones.ToList();
		while ( pending.Count > 0 )
		{
			var progressed = false;
			for ( var i = pending.Count - 1; i >= 0; i-- )
			{
				var bone = pending[i];
				if ( !authoredModel.TryGetValue( bone.Name, out var desiredModel ) )
				{
					pending.RemoveAt( i );
					progressed = true;
					continue;
				}
				if ( !string.IsNullOrWhiteSpace( bone.ParentName )
					&& skeleton.ByName.ContainsKey( bone.ParentName )
					&& !exportModel.ContainsKey( bone.ParentName ) )
				{
					continue;
				}

				var authored = authoredLocal[bone.Name];
				var bind = skeleton.GetBindLocal( bone );
				var relativeScale = new Vector3(
					ScaleRatio( authored.Scale.x, bind.Scale.x ),
					ScaleRatio( authored.Scale.y, bind.Scale.y ),
					ScaleRatio( authored.Scale.z, bind.Scale.z ) );
				Transform local;
				if ( string.IsNullOrWhiteSpace( bone.ParentName )
					|| !exportModel.TryGetValue( bone.ParentName, out var parent ) )
				{
					local = new Transform(
						desiredModel.Position,
						desiredModel.Rotation.Normal,
						relativeScale );
				}
				else
				{
					local = new Transform(
						parent.PointToLocal( desiredModel.Position ),
						(parent.Rotation.Inverse * desiredModel.Rotation).Normal,
						relativeScale );
				}

				exportLocal[bone.Name] = local;
				exportModel[bone.Name] = string.IsNullOrWhiteSpace( bone.ParentName )
					|| !exportModel.TryGetValue( bone.ParentName, out var exportParent )
						? local
						: ComposeLocal( exportParent, local );
				pending.RemoveAt( i );
				progressed = true;
			}

			if ( progressed )
				continue;

			throw new InvalidOperationException(
				$"The animation host contains a cyclic pose hierarchy near '{pending[0].Name}'." );
		}

		return exportLocal;
	}

	private static IReadOnlyDictionary<string, Transform> BuildModelTransforms(
		HostSkeleton skeleton,
		IReadOnlyDictionary<string, Transform> local )
	{
		var model = new Dictionary<string, Transform>( StringComparer.OrdinalIgnoreCase );
		var pending = skeleton.Bones.ToList();
		while ( pending.Count > 0 )
		{
			var progressed = false;
			for ( var i = pending.Count - 1; i >= 0; i-- )
			{
				var bone = pending[i];
				if ( !local.TryGetValue( bone.Name, out var boneLocal ) )
				{
					pending.RemoveAt( i );
					progressed = true;
					continue;
				}
				if ( !string.IsNullOrWhiteSpace( bone.ParentName )
					&& skeleton.ByName.ContainsKey( bone.ParentName )
					&& !model.ContainsKey( bone.ParentName ) )
				{
					continue;
				}

				model[bone.Name] = string.IsNullOrWhiteSpace( bone.ParentName )
					|| !model.TryGetValue( bone.ParentName, out var parent )
						? boneLocal
						: ComposeLocal( parent, boneLocal );
				pending.RemoveAt( i );
				progressed = true;
			}

			if ( progressed )
				continue;

			throw new InvalidOperationException(
				$"The animation host contains a cyclic pose hierarchy near '{pending[0].Name}'." );
		}

		return model;
	}

	private static Transform ComposeLocal( Transform parent, Transform local ) => new(
		parent.PointToWorld( local.Position ),
		parent.Rotation * local.Rotation,
		parent.Scale * local.Scale );

	private static float ScaleRatio( float value, float bindValue )
	{
		if ( !float.IsFinite( value ) )
			return 1.0f;

		return float.IsFinite( bindValue ) && MathF.Abs( bindValue ) > 0.000001f
			? value / bindValue
			: value;
	}

	private static void ApplyBoneVisibility(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		WeaponAnimationClip clip,
		float time,
		EvaluatedPose pose )
	{
		foreach ( var part in document.Rig.VisibilityParts.Where( x =>
			x.RenderMode == VisibilityRenderMode.BoneBranch
			&& !WeaponVisibilityEvaluator.Evaluate( x, clip, time ) ) )
		{
			var definition = document.Rig.FindBone( part.BoneId )
				?? document.Rig.FindBone( part.BoneName );
			var boneName = definition is not null
				&& definition.Id.Equals(
					document.Rig.SourceSkeletonRootId,
					StringComparison.OrdinalIgnoreCase )
					? "weapon_root"
					: definition?.Name ?? part.BoneName;
			if ( !skeleton.ByName.ContainsKey( boneName )
				|| !pose.Local.TryGetValue( boneName, out var local ) )
				continue;

			// Some ModelDoc paths normalize animated bone scale. The off-screen translation keeps
			// visibility native and deterministic even when the scale channel is discarded.
			pose.Local[boneName] = local
				.WithPosition( local.Position + BoneVisibilitySinkOffset )
				.WithScale( local.Scale * 0.0001f );
		}
	}

	public static string WriteReference( HostSkeleton skeleton )
	{
		if ( skeleton.Bones.Count == 0 )
			throw new InvalidOperationException( "The animation host skeleton contains no bones." );

		var rootId = Id( "root" );
		var modelId = Id( "model" );
		var modelTransformId = Id( "model-transform" );
		var meshDagId = Id( "mesh-dag" );
		var meshTransformId = Id( "mesh-transform" );
		var meshId = Id( "mesh" );
		var vertexDataId = Id( "vertex-data" );
		var faceSetId = Id( "face-set" );
		var materialId = Id( "material" );
		var compilerBindLocal = skeleton.BuildCompilerBindLocalTransforms();
		var builder = new StringBuilder();

		builder.AppendLine( "<!-- dmx encoding keyvalues2 4 format model 22 -->" );
		builder.AppendLine( "\"DmElement\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", rootId );
		Attribute( builder, 1, "name", "string", "root" );
		Attribute( builder, 1, "model", "element", modelId );
		Attribute( builder, 1, "skeleton", "element", modelId );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeModel\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", modelId );
		Attribute( builder, 1, "name", "string", "weapon_animation_host" );
		TransformElement( builder, 1, modelTransformId, "model", Transform.Zero );
		Attribute( builder, 1, "visible", "bool", "1" );
		ElementArray(
			builder,
			1,
			"children",
			skeleton.Bones
				.Where( bone => string.IsNullOrWhiteSpace( bone.ParentName )
					|| !skeleton.ByName.ContainsKey( bone.ParentName ) )
				.Select( bone => JointId( bone.Index ) )
				.Append( meshDagId ) );
		ElementArray( builder, 1, "jointList", skeleton.Bones.Select( bone => JointId( bone.Index ) ) );
		Attribute( builder, 1, "upAxis", "string", "Z" );
		builder.AppendLine( "\t\"axisSystem\" \"DmeAxisSystem\"" );
		builder.AppendLine( "\t{" );
		Attribute( builder, 2, "id", "elementid", Id( "axis-system" ) );
		Attribute( builder, 2, "name", "string", "" );
		Attribute( builder, 2, "upAxis", "int", "3" );
		Attribute( builder, 2, "forwardParity", "int", "1" );
		Attribute( builder, 2, "coordSys", "int", "0" );
		builder.AppendLine( "\t}" );
		builder.AppendLine( "}" );
		builder.AppendLine();

		foreach ( var bone in skeleton.Bones )
			WriteJoint( builder, skeleton, bone, compilerBindLocal[bone.Name] );

		builder.AppendLine( "\"DmeDag\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", meshDagId );
		Attribute( builder, 1, "name", "string", "host_reference_triangle" );
		TransformElement( builder, 1, meshTransformId, "host_reference_triangle", Transform.Zero );
		Attribute( builder, 1, "shape", "element", meshId );
		Attribute( builder, 1, "visible", "bool", "1" );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeMesh\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", meshId );
		Attribute( builder, 1, "name", "string", "host_reference_triangle" );
		Attribute( builder, 1, "visible", "bool", "1" );
		Attribute( builder, 1, "currentState", "element", vertexDataId );
		ElementArray( builder, 1, "baseStates", [vertexDataId] );
		builder.AppendLine( "\t\"faceSets\" \"element_array\"" );
		builder.AppendLine( "\t[" );
		builder.AppendLine( "\t\t\"DmeFaceSet\"" );
		builder.AppendLine( "\t\t{" );
		Attribute( builder, 3, "id", "elementid", faceSetId );
		Attribute( builder, 3, "name", "string", CarrierMaterial );
		IntArray( builder, 3, "faces", CarrierFaces( skeleton ) );
		builder.AppendLine( "\t\t\t\"material\" \"DmeMaterial\"" );
		builder.AppendLine( "\t\t\t{" );
		Attribute( builder, 4, "id", "elementid", materialId );
		Attribute( builder, 4, "name", "string", CarrierMaterial );
		Attribute( builder, 4, "mtlName", "string", CarrierMaterial );
		builder.AppendLine( "\t\t\t}" );
		builder.AppendLine( "\t\t}" );
		builder.AppendLine( "\t]" );
		builder.AppendLine( "}" );
		builder.AppendLine();

		WriteVertexData( builder, vertexDataId, skeleton );
		return builder.ToString();
	}

	private static void WriteAnimationJoint(
		StringBuilder builder,
		HostSkeleton skeleton,
		HostBone bone,
		string prefix )
	{
		builder.AppendLine( "\"DmeJoint\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", AnimationJointId( prefix, bone.Index ) );
		Attribute( builder, 1, "name", "string", bone.Name );
		Attribute(
			builder,
			1,
			"transform",
			"element",
			AnimationTransformId( prefix, bone.Index ) );
		Attribute( builder, 1, "shape", "element", "" );
		Attribute( builder, 1, "visible", "bool", "1" );
		ElementArray(
			builder,
			1,
			"children",
			skeleton.Bones
				.Where( child => child.ParentName.Equals(
					bone.Name,
					StringComparison.OrdinalIgnoreCase ) )
				.Select( child => AnimationJointId( prefix, child.Index ) ) );
		builder.AppendLine( "}" );
		builder.AppendLine();
	}

	private static void WriteVectorChannel(
		StringBuilder builder,
		string prefix,
		HostBone bone,
		string attribute,
		string[] times,
		string[] values )
	{
		var channelId = AnimationChannelId( prefix, bone.Index, attribute );
		var logId = Id( $"{prefix}:log:{bone.Index}:{attribute}" );
		var layerId = Id( $"{prefix}:layer:{bone.Index}:{attribute}" );
		var transformId = AnimationTransformId( prefix, bone.Index );

		WriteChannelHeader(
			builder,
			channelId,
			$"{bone.Name}_p",
			transformId,
			"position",
			logId );
		builder.AppendLine( "\"DmeVector3Log\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", logId );
		Attribute( builder, 1, "name", "string", "vector3 log" );
		ElementArray( builder, 1, "layers", [layerId] );
		Attribute( builder, 1, "curveinfo", "element", "" );
		Attribute( builder, 1, "usedefaultvalue", "bool", "0" );
		Attribute( builder, 1, "defaultvalue", "vector3", values[0] );
		TimeArray( builder, 1, "bookmarksX", [] );
		TimeArray( builder, 1, "bookmarksY", [] );
		TimeArray( builder, 1, "bookmarksZ", [] );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeVector3LogLayer\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", layerId );
		Attribute( builder, 1, "name", "string", "vector3 log" );
		TimeArray( builder, 1, "times", times );
		IntArray( builder, 1, "curvetypes", [] );
		VectorArray( builder, 1, "values", "vector3_array", values );
		Attribute( builder, 1, "compressed", "binary", "" );
		builder.AppendLine( "}" );
		builder.AppendLine();
	}

	private static void WriteQuaternionChannel(
		StringBuilder builder,
		string prefix,
		HostBone bone,
		string[] times,
		string[] values )
	{
		var attribute = "orientation";
		var channelId = AnimationChannelId( prefix, bone.Index, attribute );
		var logId = Id( $"{prefix}:log:{bone.Index}:{attribute}" );
		var layerId = Id( $"{prefix}:layer:{bone.Index}:{attribute}" );
		var transformId = AnimationTransformId( prefix, bone.Index );

		WriteChannelHeader(
			builder,
			channelId,
			$"{bone.Name}_o",
			transformId,
			attribute,
			logId );
		builder.AppendLine( "\"DmeQuaternionLog\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", logId );
		Attribute( builder, 1, "name", "string", "quaternion log" );
		ElementArray( builder, 1, "layers", [layerId] );
		Attribute( builder, 1, "curveinfo", "element", "" );
		Attribute( builder, 1, "usedefaultvalue", "bool", "0" );
		Attribute( builder, 1, "defaultvalue", "quaternion", values[0] );
		TimeArray( builder, 1, "bookmarksX", [] );
		TimeArray( builder, 1, "bookmarksY", [] );
		TimeArray( builder, 1, "bookmarksZ", [] );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeQuaternionLogLayer\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", layerId );
		Attribute( builder, 1, "name", "string", "quaternion log" );
		TimeArray( builder, 1, "times", times );
		IntArray( builder, 1, "curvetypes", [] );
		VectorArray( builder, 1, "values", "quaternion_array", values );
		Attribute( builder, 1, "compressed", "binary", "" );
		builder.AppendLine( "}" );
		builder.AppendLine();
	}

	private static void WriteFloatChannel(
		StringBuilder builder,
		string prefix,
		HostBone bone,
		string[] times,
		string[] values )
	{
		const string attribute = "scale";
		var channelId = AnimationChannelId( prefix, bone.Index, attribute );
		var logId = Id( $"{prefix}:log:{bone.Index}:{attribute}" );
		var layerId = Id( $"{prefix}:layer:{bone.Index}:{attribute}" );
		var transformId = AnimationTransformId( prefix, bone.Index );

		WriteChannelHeader(
			builder,
			channelId,
			$"{bone.Name}_s",
			transformId,
			attribute,
			logId );
		builder.AppendLine( "\"DmeFloatLog\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", logId );
		Attribute( builder, 1, "name", "string", "float log" );
		ElementArray( builder, 1, "layers", [layerId] );
		Attribute( builder, 1, "curveinfo", "element", "" );
		Attribute( builder, 1, "usedefaultvalue", "bool", "0" );
		Attribute( builder, 1, "defaultvalue", "float", values[0] );
		TimeArray( builder, 1, "bookmarks", [] );
		builder.AppendLine( "}" );
		builder.AppendLine();

		builder.AppendLine( "\"DmeFloatLogLayer\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", layerId );
		Attribute( builder, 1, "name", "string", "float log" );
		TimeArray( builder, 1, "times", times );
		IntArray( builder, 1, "curvetypes", [] );
		VectorArray( builder, 1, "values", "float_array", values );
		Attribute( builder, 1, "compressed", "binary", "" );
		builder.AppendLine( "}" );
		builder.AppendLine();
	}

	private static void WriteChannelHeader(
		StringBuilder builder,
		string channelId,
		string name,
		string transformId,
		string attribute,
		string logId )
	{
		builder.AppendLine( "\"DmeChannel\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", channelId );
		Attribute( builder, 1, "name", "string", name );
		Attribute( builder, 1, "fromElement", "element", "" );
		Attribute(
			builder,
			1,
			"fromAttribute",
			"string",
			attribute switch
			{
				"position" => "valuePosition",
				"orientation" => "valueOrientation",
				_ => "value"
			} );
		Attribute( builder, 1, "fromIndex", "int", "0" );
		Attribute( builder, 1, "toElement", "element", transformId );
		Attribute( builder, 1, "toAttribute", "string", attribute );
		Attribute( builder, 1, "toIndex", "int", "0" );
		Attribute( builder, 1, "mode", "int", "1" );
		Attribute( builder, 1, "log", "element", logId );
		builder.AppendLine( "}" );
		builder.AppendLine();
	}

	private static void WriteJoint(
		StringBuilder builder,
		HostSkeleton skeleton,
		HostBone bone,
		Transform bindLocal )
	{
		builder.AppendLine( "\"DmeJoint\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", JointId( bone.Index ) );
		Attribute( builder, 1, "name", "string", bone.Name );
		TransformElement(
			builder,
			1,
			Id( $"joint-transform:{bone.Index}" ),
			bone.Name,
			bindLocal );
		Attribute( builder, 1, "visible", "bool", "1" );
		ElementArray(
			builder,
			1,
			"children",
			skeleton.Bones
				.Where( child => child.ParentName.Equals( bone.Name, StringComparison.OrdinalIgnoreCase ) )
				.Select( child => JointId( child.Index ) ) );
		builder.AppendLine( "}" );
		builder.AppendLine();
	}

	private static void WriteVertexData(
		StringBuilder builder,
		string vertexDataId,
		HostSkeleton skeleton )
	{
		var positions = new string[skeleton.Bones.Count * 3];
		var normals = new string[positions.Length];
		var texcoords = new string[positions.Length];
		var indices = new int[positions.Length];
		var weights = new float[positions.Length];
		var blendIndices = new int[positions.Length];

		for ( var boneIndex = 0; boneIndex < skeleton.Bones.Count; boneIndex++ )
		{
			var vertex = boneIndex * 3;

			// A tiny weighted triangle keeps each host bone from being culled by ModelDoc.
			positions[vertex] = "0 0 0";
			positions[vertex + 1] = "0.001 0 0";
			positions[vertex + 2] = "0 0.001 0";
			normals[vertex] = normals[vertex + 1] = normals[vertex + 2] = "0 0 1";
			texcoords[vertex] = "0 0";
			texcoords[vertex + 1] = "1 0";
			texcoords[vertex + 2] = "0 1";
			indices[vertex] = vertex;
			indices[vertex + 1] = vertex + 1;
			indices[vertex + 2] = vertex + 2;
			weights[vertex] = weights[vertex + 1] = weights[vertex + 2] = 1.0f;
			blendIndices[vertex] = blendIndices[vertex + 1] = blendIndices[vertex + 2] = boneIndex;
		}

		builder.AppendLine( "\"DmeVertexData\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", vertexDataId );
		Attribute( builder, 1, "name", "string", "bind" );
		StringArray(
			builder,
			1,
			"vertexFormat",
			["position$0", "normal$0", "texcoord$0", "blendweights$0", "blendindices$0"] );
		Attribute( builder, 1, "jointCount", "int", "1" );
		Attribute( builder, 1, "flipVCoordinates", "bool", "1" );
		VectorArray( builder, 1, "position$0", "vector3_array", positions );
		IntArray( builder, 1, "position$0Indices", indices );
		VectorArray( builder, 1, "normal$0", "vector3_array", normals );
		IntArray( builder, 1, "normal$0Indices", indices );
		VectorArray( builder, 1, "texcoord$0", "vector2_array", texcoords );
		IntArray( builder, 1, "texcoord$0Indices", indices );
		FloatArray( builder, 1, "blendweights$0", weights );
		IntArray( builder, 1, "blendindices$0", blendIndices );
		builder.AppendLine( "}" );
	}

	private static int[] CarrierFaces( HostSkeleton skeleton )
	{
		var faces = new int[skeleton.Bones.Count * 4];
		for ( var boneIndex = 0; boneIndex < skeleton.Bones.Count; boneIndex++ )
		{
			var vertex = boneIndex * 3;
			var face = boneIndex * 4;
			faces[face] = vertex;
			faces[face + 1] = vertex + 1;
			faces[face + 2] = vertex + 2;
			faces[face + 3] = -1;
		}

		return faces;
	}

	private static void TransformElement(
		StringBuilder builder,
		int indent,
		string id,
		string name,
		Transform transform )
	{
		var tabs = new string( '\t', indent );
		builder.Append( tabs ).AppendLine( "\"transform\" \"DmeTransform\"" );
		builder.Append( tabs ).AppendLine( "{" );
		Attribute( builder, indent + 1, "id", "elementid", id );
		Attribute( builder, indent + 1, "name", "string", name );
		Attribute(
			builder,
			indent + 1,
			"position",
			"vector3",
			$"{F( transform.Position.x )} {F( transform.Position.y )} {F( transform.Position.z )}" );
		Attribute(
			builder,
			indent + 1,
			"orientation",
			"quaternion",
			$"{F( transform.Rotation.x )} {F( transform.Rotation.y )} "
			+ $"{F( transform.Rotation.z )} {F( transform.Rotation.w )}" );
		Attribute( builder, indent + 1, "scale", "float", F( transform.Scale.x ) );
		builder.Append( tabs ).AppendLine( "}" );
	}

	private static void ExternalTransformElement(
		StringBuilder builder,
		string id,
		string name,
		Transform transform )
	{
		builder.AppendLine( "\"DmeTransform\"" );
		builder.AppendLine( "{" );
		Attribute( builder, 1, "id", "elementid", id );
		Attribute( builder, 1, "name", "string", name );
		Attribute( builder, 1, "position", "vector3", Vector( transform.Position ) );
		Attribute( builder, 1, "orientation", "quaternion", Quaternion( transform.Rotation.Normal ) );
		Attribute( builder, 1, "scale", "float", F( transform.Scale.x ) );
		builder.AppendLine( "}" );
	}

	private static void ElementArray(
		StringBuilder builder,
		int indent,
		string name,
		System.Collections.Generic.IEnumerable<string> values )
	{
		var tabs = new string( '\t', indent );
		var items = values.ToArray();
		builder.Append( tabs ).Append( '"' ).Append( name ).AppendLine( "\" \"element_array\"" );
		builder.Append( tabs ).AppendLine( "[" );
		for ( var i = 0; i < items.Length; i++ )
		{
			builder.Append( '\t', indent + 1 )
				.Append( "\"element\" \"" )
				.Append( Escape( items[i] ) )
				.Append( '"' );
			if ( i < items.Length - 1 )
				builder.Append( ',' );
			builder.AppendLine();
		}
		builder.Append( tabs ).AppendLine( "]" );
	}

	private static void StringArray( StringBuilder builder, int indent, string name, string[] values )
	{
		var tabs = new string( '\t', indent );
		builder.Append( tabs ).Append( '"' ).Append( name ).AppendLine( "\" \"string_array\"" );
		builder.Append( tabs ).AppendLine( "[" );
		for ( var i = 0; i < values.Length; i++ )
		{
			builder.Append( '\t', indent + 1 ).Append( '"' ).Append( Escape( values[i] ) ).Append( '"' );
			if ( i < values.Length - 1 )
				builder.Append( ',' );
			builder.AppendLine();
		}
		builder.Append( tabs ).AppendLine( "]" );
	}

	private static void IntArray( StringBuilder builder, int indent, string name, int[] values ) =>
		VectorArray( builder, indent, name, "int_array", values.Select( x => x.ToString( Invariant ) ).ToArray() );

	private static void FloatArray( StringBuilder builder, int indent, string name, float[] values ) =>
		VectorArray( builder, indent, name, "float_array", values.Select( F ).ToArray() );

	private static void TimeArray( StringBuilder builder, int indent, string name, string[] values ) =>
		VectorArray( builder, indent, name, "time_array", values );

	private static void VectorArray(
		StringBuilder builder,
		int indent,
		string name,
		string type,
		string[] values )
	{
		var tabs = new string( '\t', indent );
		builder.Append( tabs ).Append( '"' ).Append( name ).Append( "\" \"" ).Append( type ).AppendLine( "\"" );
		builder.Append( tabs ).AppendLine( "[" );
		for ( var i = 0; i < values.Length; i++ )
		{
			builder.Append( '\t', indent + 1 ).Append( '"' ).Append( values[i] ).Append( '"' );
			if ( i < values.Length - 1 )
				builder.Append( ',' );
			builder.AppendLine();
		}
		builder.Append( tabs ).AppendLine( "]" );
	}

	private static void Attribute(
		StringBuilder builder,
		int indent,
		string name,
		string type,
		string value )
	{
		builder.Append( '\t', indent )
			.Append( '"' ).Append( name ).Append( '"' );
		if ( !string.IsNullOrWhiteSpace( type ) )
			builder.Append( " \"" ).Append( type ).Append( '"' );
		builder.Append( " \"" ).Append( Escape( value ) ).AppendLine( "\"" );
	}

	private static string JointId( int index ) => Id( $"joint:{index}" );
	private static string AnimationJointId( string prefix, int index ) =>
		Id( $"{prefix}:joint:{index}" );
	private static string AnimationTransformId( string prefix, int index ) =>
		Id( $"{prefix}:transform:{index}" );
	private static string AnimationBaseTransformId( string prefix, int index ) =>
		Id( $"{prefix}:base-transform:{index}" );
	private static string AnimationChannelId( string prefix, int index, string attribute ) =>
		Id( $"{prefix}:channel:{index}:{attribute}" );

	private static string Id( string key )
	{
		var bytes = SHA256.HashData( Encoding.UTF8.GetBytes( $"SboxWeaponAnimator.DmxReference:{key}" ) );
		return new Guid( bytes.AsSpan( 0, 16 ) ).ToString();
	}

	private static string F( float value ) => value.ToString( "0.######", Invariant );
	private static string Vector( Vector3 value ) =>
		$"{F( value.x )} {F( value.y )} {F( value.z )}";
	private static string Quaternion( Rotation value ) =>
		$"{F( value.x )} {F( value.y )} {F( value.z )} {F( value.w )}";
	private static string Escape( string value ) => value.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
}
