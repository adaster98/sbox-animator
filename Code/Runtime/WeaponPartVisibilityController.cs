#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator;

public sealed class WeaponVisibilityRuntimeClip
{
	public string SequenceName { get; set; } = "";
	public float Duration { get; set; } = 1.0f;
	public List<VisibilityTrack> Tracks { get; set; } = [];
}

public sealed class WeaponPartVisibilityController : Component
{
	private readonly Dictionary<Guid, bool> _states = [];
	private readonly HashSet<int> _appliedHiddenBones = [];
	private readonly HashSet<string> _warnedBodyGroups =
		new( StringComparer.OrdinalIgnoreCase );
	private SkinnedModelRenderer? _boundAnimationHost;

	[Property] public SkinnedModelRenderer? AnimationHost { get; set; }
	[Property] public SkinnedModelRenderer? WeaponRenderer { get; set; }
	[Property] public bool UseAnimGraphTags { get; set; } = true;
	[Property] public List<WeaponVisibilityPart> Parts { get; set; } = [];
	[Property] public List<WeaponVisibilityRuntimeClip> Clips { get; set; } = [];

	protected override void OnEnabled()
	{
		ResetStates();
		SyncAnimationHost();
	}

	protected override void OnDisabled()
	{
		UnbindAnimationHost();
		RestoreRendererDefaults();
	}

	protected override void OnDestroy()
	{
		UnbindAnimationHost();
		RestoreRendererDefaults();
	}

	protected override void OnUpdate()
	{
		SyncAnimationHost();
	}

	protected override void OnPreRender()
	{
		if (!UseAnimGraphTags)
			EvaluateDirectSequence();
		ApplyVisibility();
	}

	private void SyncAnimationHost()
	{
		if (ReferenceEquals(_boundAnimationHost, AnimationHost))
			return;

		UnbindAnimationHost();
		if (!AnimationHost.IsValid())
			return;

		AnimationHost!.OnAnimTagEvent += OnAnimTagEvent;
		_boundAnimationHost = AnimationHost;
	}

	private void UnbindAnimationHost()
	{
		if (_boundAnimationHost.IsValid())
			_boundAnimationHost!.OnAnimTagEvent -= OnAnimTagEvent;
		_boundAnimationHost = null;
	}

	private void OnAnimTagEvent(SceneModel.AnimTagEvent tagEvent)
	{
		if (tagEvent.Status is SceneModel.AnimTagStatus.End)
			return;

		foreach (var part in Parts)
		{
			if (tagEvent.Name.Equals(
					WeaponVisibilityEvaluator.VisibleTag(part.Id),
					StringComparison.OrdinalIgnoreCase))
			{
				_states[part.Id] = true;
				return;
			}

			if (tagEvent.Name.Equals(
					WeaponVisibilityEvaluator.HiddenTag(part.Id),
					StringComparison.OrdinalIgnoreCase))
			{
				_states[part.Id] = false;
				return;
			}
		}
	}

	private void EvaluateDirectSequence()
	{
		if (!AnimationHost.IsValid())
			return;

		var name = AnimationHost!.Sequence.Name ?? "";
		var clip = Clips.FirstOrDefault(x =>
			x.SequenceName.Equals(name, StringComparison.OrdinalIgnoreCase));
		if (clip is null)
		{
			ResetStates();
			return;
		}

		var authoredClip = new WeaponAnimationClip
		{
			Duration = clip.Duration,
			VisibilityTracks = clip.Tracks
		};
		foreach (var part in Parts)
			_states[part.Id] = WeaponVisibilityEvaluator.Evaluate(
				part,
				authoredClip,
				AnimationHost.Sequence.Time);
	}

	private void ApplyVisibility()
	{
		if (!WeaponRenderer.IsValid())
			return;

		var hiddenBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var part in Parts)
		{
			var visible = _states.GetValueOrDefault(part.Id, part.DefaultVisible);
			if (part.RenderMode == VisibilityRenderMode.BodyGroup)
			{
				if (!string.IsNullOrWhiteSpace(part.BodyGroupName)
					&& WeaponRenderer!.HasBodyGroups)
				{
					try
					{
						WeaponRenderer.SetBodyGroup(
							part.BodyGroupName,
							visible
								? part.VisibleBodyGroupValue
								: part.HiddenBodyGroupValue);
					}
					catch (Exception ex)
					{
						if (_warnedBodyGroups.Add(part.BodyGroupName))
						{
							Log.Warning(
								$"[Weapon Animator] bodygroup '{part.BodyGroupName}' could not be set: {ex.Message}");
						}
					}
				}
				continue;
			}

			if (!visible && !string.IsNullOrWhiteSpace(part.BoneName))
				hiddenBoneNames.Add(part.BoneName);
		}

		ApplyBoneBranchOverrides(hiddenBoneNames);
	}

	private void ApplyBoneBranchOverrides(HashSet<string> hiddenRootNames)
	{
		if (WeaponRenderer?.SceneObject is not SceneModel sceneModel)
			return;

		var hidden = new HashSet<int>();
		foreach (var rootName in hiddenRootNames)
		{
			var root = WeaponRenderer.Model.Bones.GetBone(rootName);
			if (root is null)
				continue;

			var queue = new Queue<BoneCollection.Bone>();
			queue.Enqueue(root);
			while (queue.Count > 0)
			{
				var bone = queue.Dequeue();
				if (!hidden.Add(bone.Index))
					continue;
				foreach (var child in bone.Children)
					queue.Enqueue(child);
			}
		}

		if (_appliedHiddenBones.Any(x => !hidden.Contains(x)))
			sceneModel.ClearBoneOverrides();

		_appliedHiddenBones.Clear();
		if (hidden.Count == 0)
			return;

		var collapsed = new Transform(
			Vector3.Down * 4000.0f,
			Rotation.Identity,
			Vector3.One * 0.001f);
		foreach (var index in hidden)
		{
			sceneModel.SetBoneOverride(index, in collapsed);
			_appliedHiddenBones.Add(index);
		}
	}

	private void ResetStates()
	{
		_states.Clear();
		foreach (var part in Parts)
			_states[part.Id] = part.DefaultVisible;
	}

	private void RestoreRendererDefaults()
	{
		if (WeaponRenderer?.SceneObject is SceneModel sceneModel)
			sceneModel.ClearBoneOverrides();
		_appliedHiddenBones.Clear();

		if (!WeaponRenderer.IsValid())
			return;
		foreach (var part in Parts.Where(x =>
			x.RenderMode == VisibilityRenderMode.BodyGroup
			&& !string.IsNullOrWhiteSpace(x.BodyGroupName)))
		{
			try
			{
				WeaponRenderer!.SetBodyGroup(
					part.BodyGroupName,
					part.DefaultVisible
						? part.VisibleBodyGroupValue
						: part.HiddenBodyGroupValue);
			}
			catch
			{
				// Models may change while the component is hotloaded.
			}
		}
	}
}
