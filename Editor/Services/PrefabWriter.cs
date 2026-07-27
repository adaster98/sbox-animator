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
		string hostModel,
		string weaponModel )
	{
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		var rootId = StableGuid( $"{document.DocumentId}:root" );
		var hostRendererId = StableGuid( $"{document.DocumentId}:host_renderer" );
		var weaponId = StableGuid( $"{document.DocumentId}:weapon" );
		var weaponRendererId = StableGuid( $"{document.DocumentId}:weapon_renderer" );
		var armsId = StableGuid( $"{document.DocumentId}:arms" );
		var armsRendererId = StableGuid( $"{document.DocumentId}:arms_renderer" );
		var visibilityControllerId = StableGuid( $"{document.DocumentId}:visibility_controller" );
		var muzzleId = StableGuid( $"{document.DocumentId}:muzzle" );
		var ejectId = StableGuid( $"{document.DocumentId}:eject" );

		var muzzle = Child( "muzzle", muzzleId, document.Calibration.GetAnchor( AnchorKind.Muzzle ) );
		var eject = Child( "eject", ejectId, document.Calibration.GetAnchor( AnchorKind.Eject ) );
		var weapon = Child( "source_weapon", weaponId );
		weapon["Components"]!.AsArray().Add(
			Renderer( weaponRendererId, weaponModel, ComponentReference( rootId, hostRendererId ), true ) );
		var arms = Child( "facepunch_arms", armsId );
		arms["Components"]!.AsArray().Add(
			Renderer(
				armsRendererId,
				HostSkeletonBuilder.ProductionArmsModel,
				ComponentReference( rootId, hostRendererId ),
				true,
				21 ) );

		var root = Child( $"v_{slug}_anim", rootId );
		var usesGraph = document.Output.GenerateGraph && document.Graph.GenerateGraph;
		root["Components"]!.AsArray().Add(
			Renderer( hostRendererId, hostModel, null, false, useAnimGraph: usesGraph ) );
		root["Components"]!.AsArray().Add( BaseWeaponModel(
			StableGuid( $"{document.DocumentId}:base_weapon_model" ),
			ComponentReference( rootId, hostRendererId ),
			GameObjectReference( muzzleId ),
			GameObjectReference( ejectId ) ) );
		if ( document.Rig.VisibilityParts.Count > 0 )
		{
			root["Components"]!.AsArray().Add( VisibilityController(
				visibilityControllerId,
				ComponentReference( rootId, hostRendererId ),
				ComponentReference( weaponId, weaponRendererId ),
				document,
				usesGraph ) );
		}
		root["Children"]!.AsArray().Add( weapon );
		root["Children"]!.AsArray().Add( arms );
		root["Children"]!.AsArray().Add( muzzle );
		root["Children"]!.AsArray().Add( eject );
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
			["__references"] = new JsonArray(),
			["__version"] = 2
		};

		return prefab.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) + "\n";
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

	private static JsonObject VisibilityController(
		string id,
		JsonObject animationHost,
		JsonObject weaponRenderer,
		WeaponAnimationDocument document,
		bool usesGraph )
	{
		var runtimeClips = document.Clips.Select( clip =>
			new WeaponVisibilityRuntimeClip
			{
				SequenceName = WeaponAnimationNames.SequenceName( clip ),
				Duration = clip.Duration,
				Tracks = clip.VisibilityTracks
			} ).ToList();
		return new JsonObject
		{
			["__type"] = "SboxWeaponAnimator.WeaponPartVisibilityController",
			["__guid"] = id,
			["__enabled"] = true,
			["Flags"] = 0,
			["AnimationHost"] = animationHost,
			["WeaponRenderer"] = weaponRenderer,
			["UseAnimGraphTags"] = usesGraph,
			["Parts"] = JsonSerializer.SerializeToNode( document.Rig.VisibilityParts ),
			["Clips"] = JsonSerializer.SerializeToNode( runtimeClips ),
			["OnComponentDestroy"] = null,
			["OnComponentDisabled"] = null,
			["OnComponentEnabled"] = null,
			["OnComponentFixedUpdate"] = null,
			["OnComponentStart"] = null,
			["OnComponentUpdate"] = null
		};
	}

	private static JsonObject BaseWeaponModel(
		string id,
		JsonObject renderer,
		JsonObject muzzle,
		JsonObject eject ) => new()
	{
		["__type"] = "Sandbox.BaseWeaponModel",
		["__guid"] = id,
		["__enabled"] = true,
		["Flags"] = 0,
		["Renderer"] = renderer,
		["DeploySound"] = null,
		["MuzzleGameObject"] = muzzle,
		["ShellEjectGameObject"] = eject,
		["MuzzleEffect"] = null,
		["EjectBrass"] = null,
		["TracerEffect"] = null,
		["OnComponentDestroy"] = null,
		["OnComponentDisabled"] = null,
		["OnComponentEnabled"] = null,
		["OnComponentFixedUpdate"] = null,
		["OnComponentStart"] = null,
		["OnComponentUpdate"] = null
	};

	private static JsonObject Child( string name, string id, WeaponAnchor? anchor = null )
	{
		var position = anchor?.LocalPosition ?? Vector3.Zero;
		var rotation = anchor?.LocalRotation ?? Rotation.Identity;
		return new JsonObject
		{
			["__guid"] = id,
			["__version"] = 2,
			["Flags"] = 0,
			["Name"] = name,
			["Position"] = VectorString( position ),
			["Rotation"] = RotationString( rotation ),
			["Scale"] = "1,1,1",
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

	private static JsonObject ComponentReference( string gameObject, string component ) => new()
	{
		["_type"] = "component",
		["component_id"] = component,
		["go"] = gameObject,
		["component_type"] = "SkinnedModelRenderer"
	};

	private static JsonObject GameObjectReference( string gameObject ) => new()
	{
		["_type"] = "gameobject",
		["go"] = gameObject
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

	private static string VectorString( Vector3 value ) =>
		$"{F( value.x )},{F( value.y )},{F( value.z )}";

	private static string RotationString( Rotation value ) =>
		$"{F( value.x )},{F( value.y )},{F( value.z )},{F( value.w )}";

	private static string F( float value ) =>
		value.ToString( "0.######", CultureInfo.InvariantCulture );

	private static string StableGuid( string value )
	{
		var hash = System.Security.Cryptography.MD5.HashData(
			System.Text.Encoding.UTF8.GetBytes( $"SboxWeaponAnimator:{value}" ) );
		return new Guid( hash ).ToString();
	}
}
