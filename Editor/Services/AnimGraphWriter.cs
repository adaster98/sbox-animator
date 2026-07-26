#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SboxWeaponAnimator.Editor;

public static class AnimGraphWriter
{
	public const string Header =
		"<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} " +
		"format:animgraph2:version{0f7898b8-5471-45c4-9867-cd9c46bcfdb5} -->";

	private sealed record StateSpec(
		string Name,
		WeaponClipRole Role,
		bool Loop,
		List<string> Transitions,
		bool Start = false );

	public static string Write( WeaponAnimationDocument document, string hostModelPath )
	{
		var idle = document.Clips.First( x => x.Role == WeaponClipRole.Idle );
		WeaponAnimationClip ClipFor( WeaponClipRole role )
		{
			var clip = document.Clips.FirstOrDefault( x => x.Role == role );
			return clip is not null && clip.Readiness != ClipReadiness.NotStarted
				? clip
				: idle;
		}

		var states = BuildStates( document );
		var nodes = new StringBuilder();
		var x = -960.0f;
		foreach ( var state in states )
		{
			var sequenceClip = ClipFor( state.Role );
			nodes.AppendLine( SequenceNode(
				$"seq_{state.Name}",
				WeaponAnimationNames.SequenceName( sequenceClip ),
				sequenceClip,
				state.Loop,
				x,
				96 ) );
			x += 144;
		}

		nodes.AppendLine( StateMachineNode( states ) );
		nodes.AppendLine( RootNode() );

		var parameters = string.Join( "\n", ParameterDefinitions() );
		var tags = string.Join( "\n", StandardTags( document ) );
		return $$"""
			{{Header}}
			// SboxWeaponAnimator generated graph. Node, state, parameter, and tag IDs are deterministic.
			{
				_class = "CAnimationGraph"
				m_nodeManager =
				{
					_class = "CAnimNodeManager"
					m_nodes =
					[
			{{nodes}}		]
				}
				m_pParameterList =
				{
					_class = "CAnimParameterList"
					m_Parameters =
					[
			{{parameters}}
					]
				}
				m_pTagManager =
				{
					_class = "CAnimTagManager"
					m_tags =
					[
			{{tags}}
					]
				}
				m_pMovementManager =
				{
					_class = "CAnimMovementManager"
					m_MotorList = { _class = "CAnimMotorList" m_motors = [ ] }
					m_MovementSettings =
					{
						_class = "CAnimMovementSettings"
						m_bShouldCalculateSlope = false
					}
				}
				m_pSettingsManager =
				{
					_class = "CAnimGraphSettingsManager"
					m_settingsGroups =
					[
						{ _class = "CAnimGraphGeneralSettings" m_iGridSnap = 16 },
					]
				}
				m_pActivityValuesList = { _class = "CActivityValueList" m_activities = [ ] }
				m_previewModels = [ "{{hostModelPath}}", ]
				m_boneMergeModels =
				[
					{
						m_name = "{{HostSkeletonBuilder.ProductionArmsModel}}"
						m_bEnabled = true
					},
				]
				m_cameraSettings =
				{
					m_flFov = {{F( document.Calibration.HorizontalFov )}}
					m_sLockBoneName = "camera"
					m_bLockCamera = true
					m_bViewModelCamera = false
				}
			}
			""";
	}

	private static List<StateSpec> BuildStates( WeaponAnimationDocument document )
	{
		var idleTransitions = new List<string>
		{
			Transition( [BoolCondition( "b_attack_dry", true )], "FireDry", 0.02f ),
			Transition( [BoolCondition( "b_attack", true )], "Fire", 0.02f ),
			Transition(
				[BoolCondition( "b_reload", true ), BoolCondition( "b_empty", true )],
				"ReloadEmpty",
				0.05f ),
			Transition( [BoolCondition( "b_reload", true )], "Reload", 0.05f ),
			Transition( [BoolCondition( "b_deploy", true )], "Deploy", 0.05f ),
			Transition( [BoolCondition( "b_holster", true )], "Holster", 0.05f ),
			Transition( [BoolCondition( "b_inspect", true )], "Inspect", 0.08f ),
			Transition( [BoolCondition( "b_sprint", true )], "Sprint", 0.08f ),
			Transition( [BoolCondition( "b_jump", true )], "Jump", 0.05f ),
			Transition( [BoolCondition( "b_lower_weapon", true )], "Lower", 0.08f ),
			Transition( [IntCondition( "ironsights", 1 )], "Ironsights", 0.08f ),
			Transition( [BoolCondition( "b_grab", true )], "GrabStance", 0.08f ),
			Transition( [IntCondition( "grab_action", 1 )], "GrabGesture1", 0.04f ),
			Transition( [IntCondition( "grab_action", 2 )], "GrabGesture2", 0.04f ),
			Transition( [IntCondition( "grab_action", 3 )], "GrabGesture3", 0.04f ),
			Transition( [IntCondition( "grab_action", 4 )], "GrabGesture4", 0.04f )
		};

		if ( document.Graph.ReloadProfile == ReloadProfile.Incremental )
			idleTransitions.Insert( 3, Transition( [BoolCondition( "b_reloading", true )], "ReloadEnter", 0.05f ) );

		var finishedToIdle = new List<string> { Transition( [FinishedCondition()], "Idle", 0.08f ) };
		var states = new List<StateSpec>
		{
			new( "Idle", WeaponClipRole.Idle, true, idleTransitions, true ),
			new( "Deploy", WeaponClipRole.Deploy, false, finishedToIdle ),
			new( "Fire", WeaponClipRole.Fire, false, [
				Transition( [BoolCondition( "b_attack", true )], "Fire", 0.01f ),
				Transition( [FinishedCondition()], "Idle", 0.06f )
			] ),
			new( "FireDry", WeaponClipRole.FireDry, false, finishedToIdle ),
			new( "Reload", WeaponClipRole.Reload, false, finishedToIdle ),
			new( "ReloadEmpty", WeaponClipRole.ReloadEmpty, false, finishedToIdle ),
			new( "Holster", WeaponClipRole.Holster, false, [] ),
			new( "Inspect", WeaponClipRole.Inspect, false, finishedToIdle ),
			new( "Sprint", WeaponClipRole.Sprint, true, [
				Transition( [BoolCondition( "b_sprint", false )], "Idle", 0.08f, false )
			] ),
			new( "Jump", WeaponClipRole.Jump, false, finishedToIdle ),
			new( "Lower", WeaponClipRole.Lower, true, [
				Transition( [BoolCondition( "b_lower_weapon", false )], "Idle", 0.08f, false )
			] ),
			new( "Ironsights", WeaponClipRole.Ironsights, true, [
				Transition( [IntCondition( "ironsights", 0 )], "Idle", 0.08f, false )
			] ),
			new( "GrabStance", WeaponClipRole.GrabStance, true, [
				Transition( [BoolCondition( "b_grab", false )], "Idle", 0.08f, false )
			] ),
			new( "GrabGesture1", WeaponClipRole.GrabGestureOne, false, finishedToIdle ),
			new( "GrabGesture2", WeaponClipRole.GrabGestureTwo, false, finishedToIdle ),
			new( "GrabGesture3", WeaponClipRole.GrabGestureThree, false, finishedToIdle ),
			new( "GrabGesture4", WeaponClipRole.GrabGestureFour, false, finishedToIdle )
		};

		if ( document.Graph.ReloadProfile == ReloadProfile.Incremental )
		{
			states.AddRange(
			[
				new( "ReloadEnter", WeaponClipRole.ReloadEnter, false, [
					Transition( [FinishedCondition()], "FirstShell", 0.04f )
				] ),
				new( "FirstShell", WeaponClipRole.FirstShell, false, [
					Transition( [BoolCondition( "b_reloading", false )], "ReloadExit", 0.04f ),
					Transition( [FinishedCondition()], "InsertShell", 0.04f )
				] ),
				new( "InsertShell", WeaponClipRole.InsertShell, false, [
					Transition( [BoolCondition( "b_reloading", false )], "ReloadExit", 0.04f ),
					Transition( [FinishedCondition()], "InsertShell", 0.02f )
				] ),
				new( "ReloadExit", WeaponClipRole.ReloadExit, false, finishedToIdle )
			] );
		}

		return states;
	}

	private static string SequenceNode(
		string name,
		string sequence,
		WeaponAnimationClip clip,
		bool loop,
		float x,
		float y )
	{
		var id = Id( $"node:{name}" );
		var tagSpans = string.Join( "\n", clip.Tags
			.Where( x => !string.IsNullOrWhiteSpace( x.Name ) )
			.OrderBy( x => x.StartTime )
			.ThenBy( x => x.Name )
			.Select( x => TagSpan( x, clip ) ) );
		return $$"""
						{
							key = { m_id = {{id}} }
							value =
							{
								_class = "CSequenceAnimNode"
								m_sName = "{{name}}"
								m_vecPosition = [ {{F( x )}}, {{F( y )}} ]
								m_nNodeID = { m_id = {{id}} }
								m_sNote = ""
								m_tagSpans =
								[
			{{tagSpans}}
								]
								m_sequenceName = "{{sequence}}"
								m_playbackSpeed = 1.0
								m_bLoop = {{loop.ToString().ToLowerInvariant()}}
							}
						},
			""";
	}

	private static string TagSpan( AnimationTag tag, WeaponAnimationClip clip )
	{
		var duration = MathF.Max( clip.Duration, 0.0001f );
		var start = Math.Clamp( tag.StartTime / duration, 0, 1 );
		var tagDuration = tag.Kind == AnimationTagKind.Point
			? MathF.Min( 1.0f / MathF.Max( clip.SampleRate, 1 ) / duration, 1.0f - start )
			: Math.Clamp( (tag.EndTime - tag.StartTime) / duration, 0, 1.0f - start );
		return $$"""
									{
										_class = "CAnimTagSpan"
										m_id = { m_id = {{Id( $"tag:{tag.Name}" )}} }
										m_fStartCycle = {{F( start )}}
										m_fDuration = {{F( tagDuration )}}
									},
			""";
	}

	private static string StateMachineNode( IEnumerable<StateSpec> states )
	{
		var stateText = string.Join( "\n", states.Select( StateNode ) );
		var id = Id( "node:StateMachine" );
		return $$"""
						{
							key = { m_id = {{id}} }
							value =
							{
								_class = "CStateMachineAnimNode"
								m_sName = "Weapon States"
								m_vecPosition = [ -224.0, 304.0 ]
								m_nNodeID = { m_id = {{id}} }
								m_sNote = ""
								m_states =
								[
			{{stateText}}
								]
							}
						},
			""";
	}

	private static string StateNode( StateSpec state )
	{
		var transitions = string.Join( "\n", state.Transitions );
		return $$"""
									{
										_class = "CAnimState"
										m_transitions =
										[
			{{transitions}}
										]
										m_tags = [ ]
										m_tagBehaviors = [ ]
										m_name = "{{state.Name}}"
										m_inputConnection =
										{
											m_nodeID = { m_id = {{Id( $"node:seq_{state.Name}" )}} }
											m_outputID = { m_id = 4294967295 }
										}
										m_stateID = { m_id = {{Id( $"state:{state.Name}" )}} }
										m_position = [ 0.0, 0.0 ]
										m_bIsStartState = {{state.Start.ToString().ToLowerInvariant()}}
										m_bIsEndtState = false
										m_bIsPassthrough = false
										m_bIsRootMotionExclusive = false
										m_bAlwaysEvaluate = false
									},
			""";
	}

	private static string RootNode()
	{
		var id = Id( "node:Root" );
		return $$"""
						{
							key = { m_id = {{id}} }
							value =
							{
								_class = "CRootAnimNode"
								m_sName = "Output"
								m_vecPosition = [ 48.0, 256.0 ]
								m_nNodeID = { m_id = {{id}} }
								m_sNote = ""
								m_inputConnection =
								{
									m_nodeID = { m_id = {{Id( "node:StateMachine" )}} }
									m_outputID = { m_id = 4294967295 }
								}
							}
						},
			""";
	}

	private static string Transition(
		IEnumerable<string> conditions,
		string destination,
		float blend,
		bool reset = true )
	{
		var conditionText = string.Join( "\n", conditions );
		return $$"""
											{
												_class = "CAnimStateTransition"
												m_conditions =
												[
			{{conditionText}}
												]
												m_blendDuration = {{F( blend )}}
												m_destState = { m_id = {{Id( $"state:{destination}" )}} }
												m_bReset = {{reset.ToString().ToLowerInvariant()}}
												m_resetCycleOption = "Beginning"
												m_flFixedCycleValue = 0.0
												m_blendCurve =
												{
													m_vControlPoint1 = [ 0.5, 0.0 ]
													m_vControlPoint2 = [ 0.5, 1.0 ]
												}
												m_bForceFootPlant = false
												m_bDisabled = false
												m_bRandomTimeBetween = false
												m_flRandomTimeStart = 0.0
												m_flRandomTimeEnd = 0.0
											},
			""";
	}

	private static string BoolCondition( string name, bool value ) => $$"""
													{
														_class = "CParameterAnimCondition"
														m_comparisonOp = 0
														m_paramID = { m_id = {{Id( $"param:{name}" )}} }
														m_comparisonValue = { m_nType = 1 m_data = {{value.ToString().ToLowerInvariant()}} }
													},
			""";

	private static string IntCondition( string name, int value ) => $$"""
													{
														_class = "CParameterAnimCondition"
														m_comparisonOp = 0
														m_paramID = { m_id = {{Id( $"param:{name}" )}} }
														m_comparisonValue = { m_nType = 3 m_data = {{value}} }
													},
			""";

	private static string FinishedCondition() => """
													{
														_class = "CFinishedCondition"
														m_comparisonOp = 0
														m_option = "FinishedConditionOption_OnAlmostFinished"
														m_bIsFinished = true
													},
			""";

	private static IEnumerable<string> ParameterDefinitions()
	{
		var pulseBools = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
		{
			"b_attack", "b_attack_dry", "b_jump", "b_reload", "b_deploy", "b_inspect",
			"b_reloading_shell", "b_reloading_first_shell"
		};
		var bools = new[]
		{
			"b_grounded", "b_jump", "b_sprint", "b_attack", "b_attack_dry", "b_attack_has_hit",
			"b_reload", "b_empty", "b_deploy", "b_deploy_skip", "b_twohanded",
			"b_lower_weapon", "b_holster", "b_grab", "b_inspect", "b_reloading",
			"b_reloading_shell", "b_reloading_first_shell"
		};
		foreach ( var name in bools )
			yield return BoolParameter( name, pulseBools.Contains( name ) );

		var floats = new (string Name, float Default, float Minimum, float Maximum)[]
		{
			("move_bob", 0, 0, 1),
			("move_bob_cycle_control", 0, 0, 1),
			("move_x", 0, -1, 1),
			("move_y", 0, -1, 1),
			("move_z", 0, -1, 1),
			("attack_hold", 0, 0, 1),
			("ironsights_fire_scale", 0, 0, 1),
			("camera_position_scale", 1, 0, 2),
			("camera_rotation_scale", 1, 0, 2),
			("speed_reload", 1, 0.05f, 5),
			("speed_deploy", 1, 0.05f, 5),
			("speed_ironsights", 1, 0.05f, 5),
			("speed_grab", 1, 0.05f, 5),
			("aim_pitch_inertia", 0, -45, 45),
			("aim_yaw_inertia", 0, -45, 45)
		};
		foreach ( var item in floats )
			yield return FloatParameter( item.Name, item.Default, item.Minimum, item.Maximum );

		yield return EnumParameter( "ironsights", ["Hip", "ADS"] );
		yield return EnumParameter( "firing_mode", ["Safe", "Single", "Burst", "Automatic"] );
		yield return EnumParameter( "weapon_pose", ["Default", "Alternate"] );
		yield return EnumParameter( "grab_action", ["None", "Sweep Down", "Sweep Right", "Sweep Left", "Push"] );
		yield return EnumParameter( "deploy_type", ["Default", "Alternate"] );
		yield return EnumParameter( "reload_type", ["Default", "Alternate"] );
		yield return EnumParameter( "skeleton", ["Human", "Citizen"] );
	}

	private static string BoolParameter( string name, bool autoReset ) => $$"""
						{
							_class = "CBoolAnimParameter"
							m_name = "{{name}}"
							m_id = { m_id = {{Id( $"param:{name}" )}} }
							m_previewButton = "ANIMPARAM_BUTTON_NONE"
							m_bUseMostRecentValue = false
							m_bAutoReset = {{autoReset.ToString().ToLowerInvariant()}}
							m_bDefaultValue = false
						},
			""";

	private static string FloatParameter( string name, float value, float min, float max ) => $$"""
						{
							_class = "CFloatAnimParameter"
							m_name = "{{name}}"
							m_id = { m_id = {{Id( $"param:{name}" )}} }
							m_previewButton = "ANIMPARAM_BUTTON_NONE"
							m_bUseMostRecentValue = false
							m_bAutoReset = false
							m_fDefaultValue = {{F( value )}}
							m_fMinValue = {{F( min )}}
							m_fMaxValue = {{F( max )}}
						},
			""";

	private static string EnumParameter( string name, IEnumerable<string> choices )
	{
		var options = string.Join( "\n", choices.Select( x => $"\t\t\t\t\t\t\"{x}\"," ) );
		return $$"""
						{
							_class = "CEnumAnimParameter"
							m_name = "{{name}}"
							m_id = { m_id = {{Id( $"param:{name}" )}} }
							m_previewButton = "ANIMPARAM_BUTTON_NONE"
							m_bUseMostRecentValue = false
							m_bAutoReset = {{(name == "grab_action").ToString().ToLowerInvariant()}}
							m_defaultValue = 0
							m_enumOptions =
							[
			{{options}}
							]
						},
			""";
	}

	private static IEnumerable<string> StandardTags( WeaponAnimationDocument document )
	{
		var tags = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
		{
			"attack_discouraged",
			"holster_finished",
			"reload_bodygroup",
			"reload_increment"
		};

		foreach ( var tag in document.Clips.SelectMany( x => x.Tags ) )
			tags.Add( tag.Name );

		foreach ( var name in tags.OrderBy( x => x ) )
		{
			yield return $$"""
						{
							_class = "CStringAnimTag"
							m_name = "{{name}}"
							m_tagID = { m_id = {{Id( $"tag:{name}" )}} }
						},
			""";
		}
	}

	public static uint Id( string value )
	{
		uint crc = 0xFFFFFFFF;
		foreach ( var data in Encoding.UTF8.GetBytes( value ) )
		{
			crc ^= data;
			for ( var bit = 0; bit < 8; bit++ )
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
		}

		var result = (crc ^ 0xFFFFFFFF) & 0x7FFFFFFF;
		return result == 0 ? 1u : result;
	}

	private static string F( float value ) =>
		value.ToString( "0.######", CultureInfo.InvariantCulture );
}
