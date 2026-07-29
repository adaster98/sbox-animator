#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class PrefabWriter
{
	public static string Write(
		WeaponAnimationDocument document,
		string hostModel )
	{
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		var rootId = StableGuid( $"{document.DocumentId}:root" );
		var hostRendererId = StableGuid( $"{document.DocumentId}:host_renderer" );
		var armsId = StableGuid( $"{document.DocumentId}:arms" );
		var armsRendererId = StableGuid( $"{document.DocumentId}:arms_renderer" );
		var cameraId = StableGuid( $"{document.DocumentId}:camera" );

		var root = Child( $"v_{slug}_anim", rootId );
		var usesGraph = document.Output.GenerateGraph && document.Graph.GenerateGraph;
		root["Components"]!.AsArray().Add(
			Renderer( hostRendererId, hostModel, null, true, useAnimGraph: usesGraph ) );

		// Match Facepunch viewmodels: one visible animated root, bone-merged arms, and camera.
		var arms = Child( "v_first_person_arms_human", armsId );
		arms["Components"]!.AsArray().Add(
			Renderer(
				armsRendererId,
				HostSkeletonBuilder.ProductionArmsModel,
				ComponentReference( rootId, hostRendererId ),
				false,
				21 ) );
		root["Children"]!.AsArray().Add( arms );
		root["Children"]!.AsArray().Add( Child( "camera", cameraId ) );
		AddAnchorChildren( document, root["Children"]!.AsArray() );
		root["__properties"] = SceneProperties();
		root["__variables"] = new JsonArray();

		var prefab = new JsonObject
		{
			["RootObject"] = root,
			["ResourceVersion"] = 2,
			["ShowInMenu"] = false,
			["MenuPath"] = null,
			["MenuIcon"] = null,
			["DontBreakAsTemplate"] = false,
			["__references"] = PackageReferences(
				HostSkeletonBuilder.ProductionArmsModel,
				hostModel ),
			["__version"] = 2
		};

		return prefab.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) + "\n";
	}

	private static void AddAnchorChildren(
		WeaponAnimationDocument document,
		JsonArray children )
	{
		var placement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );
		foreach ( var anchor in document.Calibration.Anchors
			.Where( anchor => anchor.Kind is AnchorKind.Muzzle
				or AnchorKind.Eject
				or AnchorKind.Custom )
			.OrderBy( anchor => anchor.Kind )
			.ThenBy( anchor => anchor.Name, StringComparer.OrdinalIgnoreCase ) )
		{
			var name = WeaponAnimationNames.AttachmentName( anchor );
			if ( string.IsNullOrWhiteSpace( name ) )
				continue;

			var transform = new Transform(
				placement.PointToWorld( anchor.LocalPosition ),
				placement.Rotation * anchor.LocalRotation );
			children.Add( Child(
				name,
				StableGuid( $"{document.DocumentId}:anchor:{anchor.Id}" ),
				transform ) );
		}
	}

	/// <summary>
	/// Cloud models only resolve on another machine when the prefab declares their package.
	/// </summary>
	private static JsonArray PackageReferences( params string[] modelPaths )
	{
		var references = new JsonArray();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in modelPaths )
		{
			if ( string.IsNullOrWhiteSpace( path ) )
				continue;

			string? reference;
			try
			{
				var package = global::Editor.AssetSystem.FindByPath( path )?.Package;
				if ( package is null )
					continue;

				var version = package.Revision?.VersionId;
				reference = version is null
					? package.FullIdent
					: $"{package.FullIdent}#{version}";
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] could not resolve a package for '{path}': {ex.Message}" );
				continue;
			}

			if ( !string.IsNullOrWhiteSpace( reference ) && seen.Add( reference ) )
				references.Add( reference );
		}

		return references;
	}

	private static JsonObject Renderer(
		string id,
		string model,
		JsonObject? boneMerge,
		bool gameLayer,
		ulong bodyGroups = ulong.MaxValue,
		bool useAnimGraph = true ) => new()
	{
		["__type"] = "Sandbox.SkinnedModelRenderer",
		["__guid"] = id,
		["__enabled"] = true,
		["Flags"] = 0,
		["AnimationGraph"] = null,
		["BodyGroups"] = bodyGroups,
		["BoneMergeTarget"] = boneMerge,
		["CreateAttachments"] = false,
		["CreateBoneObjects"] = false,
		["LodOverride"] = null,
		["MaterialGroup"] = null,
		["MaterialOverride"] = null,
		["Materials"] = null,
		["Model"] = model,
		["Morphs"] = new JsonObject(),
		["OnComponentDestroy"] = null,
		["OnComponentDisabled"] = null,
		["OnComponentEnabled"] = null,
		["OnComponentFixedUpdate"] = null,
		["OnComponentStart"] = null,
		["OnComponentUpdate"] = null,
		["Parameters"] = new JsonObject
		{
			["bools"] = new JsonObject(),
			["ints"] = new JsonObject(),
			["floats"] = new JsonObject(),
			["vectors"] = new JsonObject(),
			["rotations"] = new JsonObject()
		},
		["PlaybackRate"] = 1,
		["RenderOptions"] = new JsonObject
		{
			["GameLayer"] = gameLayer,
			["OverlayLayer"] = false,
			["BloomLayer"] = false,
			["AfterUILayer"] = false
		},
		["RenderType"] = "On",
		["Sequence"] = new JsonObject
		{
			["Name"] = null,
			["Looping"] = true,
			["Blending"] = false
		},
		["Tint"] = "1,1,1,1",
		["UseAnimGraph"] = useAnimGraph
	};

	private static JsonObject Child(
		string name,
		string id,
		Transform? transform = null )
	{
		var value = transform ?? Transform.Zero;
		var rotation = value.Rotation.Normal;
		return new JsonObject
	{
		["__guid"] = id,
		["__version"] = 2,
		["Flags"] = 0,
		["Name"] = name,
		["Position"] = Vector( value.Position ),
		["Rotation"] = $"{F( rotation.x )},{F( rotation.y )},{F( rotation.z )},{F( rotation.w )}",
		["Scale"] = Vector( value.Scale ),
		["Tags"] = "",
		["Enabled"] = true,
		["NetworkMode"] = 2,
		["NetworkFlags"] = 0,
		["NetworkOrphaned"] = 0,
		["NetworkTransmit"] = true,
		["OwnerTransfer"] = 1,
		["Components"] = new JsonArray(),
		["Children"] = new JsonArray()
	};
	}

	private static string Vector( Vector3 value ) =>
		$"{F( value.x )},{F( value.y )},{F( value.z )}";

	private static string F( float value ) =>
		value.ToString( "0.######", CultureInfo.InvariantCulture );

	private static JsonObject ComponentReference( string gameObject, string component ) => new()
	{
		["_type"] = "component",
		["component_id"] = component,
		["go"] = gameObject,
		["component_type"] = "SkinnedModelRenderer"
	};

	private static JsonObject SceneProperties() => new()
	{
		["NetworkInterpolation"] = true,
		["TimeScale"] = 1,
		["WantsSystemScene"] = true,
		["Metadata"] = new JsonObject(),
		["NavMesh"] = new JsonObject
		{
			["Enabled"] = false,
			["IncludeStaticBodies"] = true,
			["IncludeKeyframedBodies"] = true,
			["EditorAutoUpdate"] = false,
			["AgentHeight"] = 64,
			["AgentRadius"] = 16,
			["AgentStepSize"] = 18,
			["AgentMaxSlope"] = 40,
			["ExcludedBodies"] = "",
			["IncludedBodies"] = "",
			["DeferGeneration"] = false,
			["CustomBounds"] = false
		}
	};

	private static string StableGuid( string value )
	{
		var hash = System.Security.Cryptography.MD5.HashData(
			System.Text.Encoding.UTF8.GetBytes( $"SboxWeaponAnimator:{value}" ) );
		return new Guid( hash ).ToString();
	}
}
