#nullable enable annotations

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class DmxWriter
{
	private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
	private const string CarrierMaterial = "materials/tools/toolsinvisible.vmat";

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
		Attribute( builder, 2, "forwardParity", "int", "-2" );
		Attribute( builder, 2, "coordSys", "int", "0" );
		builder.AppendLine( "\t}" );
		builder.AppendLine( "}" );
		builder.AppendLine();

		foreach ( var bone in skeleton.Bones )
			WriteJoint( builder, skeleton, bone );

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

	private static void WriteJoint( StringBuilder builder, HostSkeleton skeleton, HostBone bone )
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
			skeleton.GetBindLocal( bone ) );
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

	private static string Id( string key )
	{
		var bytes = SHA256.HashData( Encoding.UTF8.GetBytes( $"SboxWeaponAnimator.DmxReference:{key}" ) );
		return new Guid( bytes.AsSpan( 0, 16 ) ).ToString();
	}

	private static string F( float value ) => value.ToString( "0.######", Invariant );
	private static string Escape( string value ) => value.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
}
