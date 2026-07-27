#nullable enable annotations

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

[EditorForAssetType( "wepanim" )]
public sealed class WeaponAnimatorWindow : DockWindow, IAssetEditor
{
	private readonly WeaponAnimatorController _controller = new();
	private readonly WeaponSourceImporter _importer = new();
	private readonly AssetGenerationService _generator = new();
	private Asset? _asset;
	private WeaponAnimationAsset? _resource;
	private Widget? _root;
	private WeaponAnimatorToolbar? _toolbar;
	private WeaponAnimatorViewport? _viewport;
	private ValidationStatusPanel? _statusPanel;
	private Splitter? _horizontalSplitter;
	private Splitter? _verticalSplitter;
	private Splitter? _animationRightSplitter;
	private Splitter? _animationOuterSplitter;
	private Button? _validationButton;
	private Button? _playButton;
	private bool _allowClose;
	private bool _rebaseOnConfirm;
	private bool _rebuilding;
	private WeaponAnimationMigrationResult? _migration;
	private bool _migrationBackupRequired;

	public bool CanOpenMultipleAssets => false;
	public void SelectMember( string memberName ) { }

	public WeaponAnimatorWindow()
	{
		DeleteOnClose = true;
		WindowTitle = "S&box Weapon Animator";
		Title = WindowTitle;
		Size = new Vector2( 1600, 940 );
		MinimumSize = new Vector2( 1200, 720 );
		StateCookie = "SboxWeaponAnimator.Window";
		SetWindowIcon( "animation" );

		_controller.DocumentChanged += OnDocumentChanged;
		_controller.DirtyChanged += OnDirtyChanged;
		_controller.SelectionChanged += RefreshToolbarState;
		_controller.PlaybackChanged += RefreshToolbarState;

		BuildMenuBar();
		BuildWorkspace();
		Show();
	}

	public void AssetOpen( Asset asset )
	{
		_asset = asset;
		_resource = asset?.LoadResource<WeaponAnimationAsset>() ?? new WeaponAnimationAsset();
		var document = _resource.Document ?? WeaponAnimationDocument.CreateDefault( asset?.Name ?? "New Weapon" );
		_migration = MigrateAndRepair( document );
		_migrationBackupRequired = _migration.Changed;
		_controller.SetDocument( document );
		if ( _migration.Changed )
			_controller.ReplaceWithoutHistory( document, true );

		if ( document.ActiveStage == WeaponAnimatorStage.Animate
			&& document.Source.Compiled )
		{
			PreviewHostBuilder.Build( document );
		}

		BuildWorkspace();
		if ( !OfferRecovery() )
			OfferCachedImportRecovery();
		if ( _migration.Changed )
			_statusPanel?.SetMessage( _migration.Summary, ValidationSeverity.Warning );
		RefreshTitle();
	}

	protected override bool OnClose()
	{
		SaveWorkspaceState();
		if ( _allowClose || !_controller.IsDirty )
		{
			DestroyWorkspace();
			return true;
		}

		Dialog.AskConfirm(
			() =>
			{
				if ( Save() )
				CloseAfterPrompt();
			},
			() =>
			{
				Dialog.AskConfirm(
					CloseAfterPrompt,
					"Discard all unsaved changes to this Weapon Animation Project?",
					"Discard Changes",
					"Discard",
					"Cancel" );
			},
			"Save changes before closing this Weapon Animation Project?",
			"Unsaved Weapon Animation Project",
			"Save",
			"More Options" );
		return false;
	}

	[Shortcut( "editor.save", "Ctrl+S", ShortcutType.Window )]
	private void ShortcutSave() => Save();

	[Shortcut( "editor.undo", "Ctrl+Z", ShortcutType.Window )]
	private void ShortcutUndo() => _controller.Undo();

	[Shortcut( "editor.redo", "Ctrl+Y", ShortcutType.Window )]
	private void ShortcutRedo() => _controller.Redo();

	[Shortcut( "weaponanim.copykeys", "Ctrl+C", ShortcutType.Window )]
	private void ShortcutCopy() => _controller.CopySelectedKeys();

	[Shortcut( "weaponanim.pastekeys", "Ctrl+V", ShortcutType.Window )]
	private void ShortcutPaste() => _controller.PasteKeys();

	[Shortcut( "weaponanim.cutkeys", "Ctrl+X", ShortcutType.Window )]
	private void ShortcutCut() => _controller.CutSelectedKeys();

	[Shortcut( "weaponanim.key", "K", ShortcutType.Window )]
	private void ShortcutKey()
	{
		var document = _controller.Document;
		if ( !string.IsNullOrWhiteSpace( document.Workspace.SelectedControl ) )
		{
			var target = document.Workspace.SelectedControl switch
			{
				"@primary_hand" => document.Binding.PrimaryHand,
				"@support_hand" => document.Binding.SupportHand,
				"@primary_elbow" => document.Binding.PrimaryElbowPole,
				"@support_elbow" => document.Binding.SupportElbowPole,
				_ => null
			};
			if ( target is not null )
				_controller.UpsertSelectedTransformKey(
					document.Workspace.SelectedControl,
					RigControlKind.Arm,
					target.Transform );
		}
	}

	[Shortcut( "weaponanim.move", "W", ShortcutType.Window )]
	private void ShortcutMove() =>
		_viewport?.SetTransformMode( WeaponAnimatorTransformMode.Move );

	[Shortcut( "weaponanim.rotate", "E", ShortcutType.Window )]
	private void ShortcutRotate() =>
		_viewport?.SetTransformMode( WeaponAnimatorTransformMode.Rotate );

	[Shortcut( "weaponanim.scale", "R", ShortcutType.Window )]
	private void ShortcutScale() =>
		_viewport?.SetTransformMode( WeaponAnimatorTransformMode.Scale );

	[EditorEvent.Hotload]
	public void OnHotload()
	{
		SaveWorkspaceState();
		MenuBar.Clear();
		BuildMenuBar();
		if ( _controller.Document.Source.Compiled )
			PreviewHostBuilder.Build( _controller.Document );
		BuildWorkspace();
	}

	private void BuildMenuBar()
	{
		var file = MenuBar.AddMenu( "File" );
		file.AddOption( "New", "note_add", WeaponAnimatorLauncher.CreateNew );
		file.AddOption( "Open…", "folder_open", WeaponAnimatorLauncher.OpenExisting );
		file.AddSeparator();
		file.AddOption( "Save", "save", () => Save(), "editor.save" );
		file.AddOption( "Save As…", "save_as", SaveAs );
		file.AddOption( "Generate Assets", "build", GenerateAssets );
		file.AddSeparator();
		file.AddOption( "Close", "close", Close );

		var edit = MenuBar.AddMenu( "Edit" );
		edit.AddOption( "Undo", "undo", _controller.Undo, "editor.undo" );
		edit.AddOption( "Redo", "redo", _controller.Redo, "editor.redo" );
		edit.AddSeparator();
		edit.AddOption( "Cut Keys", "content_cut", _controller.CutSelectedKeys );
		edit.AddOption( "Copy Keys", "content_copy", _controller.CopySelectedKeys );
		edit.AddOption( "Paste Keys", "content_paste", _controller.PasteKeys );
		edit.AddSeparator();
		edit.AddOption( "Preferences…", "tune", OpenPreferences );

		var view = MenuBar.AddMenu( "View" );
		view.AddOption( "Calibrate", "straighten", RequestCalibrationStage );
		view.AddOption( "Animate", "animation", () => SwitchStage( WeaponAnimatorStage.Animate ) );
		view.AddSeparator();
		view.AddOption( "Toggle Guides", "aspect_ratio", () =>
			_controller.Mutate( "Viewport guides", d => d.Workspace.ShowGuides = !d.Workspace.ShowGuides ) );
		view.AddOption( "Toggle Skeleton", "accessibility_new", () =>
			_controller.Mutate( "Skeleton overlay", d => d.Workspace.ShowSkeleton = !d.Workspace.ShowSkeleton ) );
		view.AddOption( "Toggle Onion Skins", "filter_none", () =>
			_controller.Mutate( "Onion skins", d => d.Workspace.ShowOnionSkins = !d.Workspace.ShowOnionSkins ) );
		view.AddOption( "Viewmodel Camera Preview", "videocam", () =>
			_controller.Mutate(
				"Preview camera",
				d => d.Workspace.FirstPersonPreview = !d.Workspace.FirstPersonPreview ) );
		view.AddSeparator();
		view.AddOption( "Reset Workspace", "restart_alt", ResetWorkspace );

		var tools = MenuBar.AddMenu( "Tools" );
		tools.AddOption( "Validate", "rule", Validate );
		tools.AddOption( "Rebuild Preview Rig", "refresh", RebuildPreviewHost );
		tools.AddOption( "Reimport Source", "published_with_changes", ReimportSource );
		tools.AddOption( "Open Generated Folder", "folder", OpenGeneratedFolder );
	}

	private void BuildWorkspace()
	{
		if ( _rebuilding )
			return;
		_rebuilding = true;
		SaveWorkspaceState();
		DestroyWorkspace();

		_root = new Widget( this );
		_root.SetStyles( "background-color: rgb(13,15,17); border: none;" );
		_root.Layout = Layout.Column();
		_root.Layout.Margin = 0;
		_root.Layout.Spacing = 4;

		_toolbar = new WeaponAnimatorToolbar( _root );
		BuildToolbar();
		_root.Layout.Add( _toolbar );

		_viewport = new WeaponAnimatorViewport( _controller );
		_viewport.StatusChanged += ( message ) => _statusPanel?.SetMessage( message );
		_viewport.LegacyIdleRepaired += () => _migrationBackupRequired = true;

		if ( _controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate )
			BuildCalibrationLayout();
		else
			BuildAnimationLayout();

		Canvas = _root;
		_rebuilding = false;
		RefreshToolbarState();
	}

	private void BuildToolbar()
	{
		if ( _toolbar is null )
			return;
		_toolbar.Clear();
		_toolbar.AddLeft( "Save", "save", () => Save() );
		_toolbar.AddLeft( "Generate", "build", GenerateAssets, true );
		if ( _controller.Document.ActiveStage == WeaponAnimatorStage.Animate )
		{
			_playButton = _toolbar.AddLeft( "Play", "play_arrow", TogglePlayback );
		}
		_toolbar.AddLeft( "Undo", "undo", _controller.Undo, overflowAtNarrowWidth: true );
		_toolbar.AddLeft( "Redo", "redo", _controller.Redo, overflowAtNarrowWidth: true );

		var calibrate = _toolbar.AddCenter(
			"1  Calibrate",
			"straighten",
			RequestCalibrationStage,
			_controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate );
		calibrate.IsToggle = true;
		calibrate.IsChecked = _controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate;
		var animate = _toolbar.AddCenter(
			"2  Animate",
			"animation",
			() => SwitchStage( WeaponAnimatorStage.Animate ),
			_controller.Document.ActiveStage == WeaponAnimatorStage.Animate );
		animate.IsToggle = true;
		animate.IsChecked = _controller.Document.ActiveStage == WeaponAnimatorStage.Animate;

		_validationButton = _toolbar.AddRight( "Validate", "rule", Validate );
		_toolbar.BalanceCenter();
	}

	private void BuildCalibrationLayout()
	{
		if ( _root is null || _viewport is null )
			return;

		var rigPanel = new RigAuditPanel( _controller );
		rigPanel.ImportRequested += ImportSource;
		rigPanel.ResetTestPoseRequested += _viewport.ResetTestPose;
		rigPanel.RigReviewConfirmed += RebuildPreviewHost;

		var inspector = new CalibrationInspectorPanel( _controller );
		inspector.PickRequested += _viewport.SetPickMode;
		inspector.AutoAlignRequested += AutoAlign;
		inspector.ConfirmRequested += ConfirmCalibration;
		inspector.RebuildPreviewRequested += RebuildPreviewHost;
		inspector.SetModelDimensions( _viewport.ModelDimensions );
		_viewport.ModelDimensionsChanged += inspector.SetModelDimensions;

		_statusPanel = new ValidationStatusPanel();
		var report = WeaponAnimationValidator.ValidateCalibration( _controller.Document );
		_statusPanel.SetReport( report );

		var left = new PanelChrome( "RIG AUDIT", "account_tree", rigPanel );
		var center = new PanelChrome( "3D CALIBRATION", "view_in_ar", _viewport );
		var right = new PanelChrome( "CALIBRATION", "tune", inspector );
		var bottom = new PanelChrome( "VALIDATION + IMPORT", "fact_check", _statusPanel );
		left.MinimumSize = new Vector2( 260, 200 );
		left.MaximumSize = new Vector2( 520, 10000 );
		right.MinimumSize = new Vector2( 310, 200 );
		right.MaximumSize = new Vector2( 560, 10000 );
		center.MinimumSize = new Vector2( 420, 240 );
		bottom.MinimumSize = new Vector2( 200, 55 );
		bottom.MaximumSize = new Vector2( 10000, 190 );
		BuildSplitLayout( left, center, right, bottom, true );
	}

	private void BuildAnimationLayout()
	{
		if ( _root is null || _viewport is null )
			return;

		var rigBrowser = new RigBrowserPanel( _controller );
		var inspector = new SelectedControlInspectorPanel( _controller );
		var clips = new ClipRackPanel(
			_controller,
			clipsOnly: true,
			showClipHeader: false );
		var timeline = new AnimationTimelinePanel( _controller );
		clips.StatusChanged += ( message, severity ) => _statusPanel?.SetMessage( message, severity );
		inspector.StatusChanged += ( message, severity ) => _statusPanel?.SetMessage( message, severity );
		_statusPanel = new ValidationStatusPanel();
		_statusPanel.SetReport( WeaponAnimationValidator.ValidateForGeneration( _controller.Document ) );

		var left = new PanelChrome( "RIG BROWSER", "account_tree", rigBrowser );
		var center = new PanelChrome( "3D ANIMATION", "view_in_ar", _viewport );
		var right = new PanelChrome( "SELECTED CONTROL", "tune", inspector );
		var clipRack = new PanelChrome( "CLIP RACK", "video_library", clips );
		var bottom = new PanelChrome( "DOPE SHEET · CURVES · TAGS", "timeline", timeline );
		left.MinimumSize = new Vector2( 330, 240 );
		left.MaximumSize = new Vector2( 540, 10000 );
		right.MinimumSize = new Vector2( 370, 240 );
		right.MaximumSize = new Vector2( 600, 10000 );
		clipRack.MinimumSize = new Vector2( 370, 260 );
		clipRack.MaximumSize = new Vector2( 600, 10000 );
		center.MinimumSize = new Vector2( 420, 260 );
		bottom.MinimumSize = new Vector2( 300, 220 );
		bottom.MaximumSize = new Vector2( 10000, 520 );
		BuildAnimationSplitLayout( left, center, right, clipRack, bottom );
	}

	private void BuildAnimationSplitLayout(
		Widget left,
		Widget center,
		Widget inspector,
		Widget clips,
		Widget timeline )
	{
		if ( _root is null )
			return;

		_verticalSplitter = new Splitter( _root )
		{
			IsVertical = true,
			OpaqueResize = true,
			HandleWidth = 4
		};
		_horizontalSplitter = new Splitter( _root )
		{
			IsHorizontal = true,
			OpaqueResize = true,
			HandleWidth = 4
		};
		_horizontalSplitter.AddWidget( left );
		_horizontalSplitter.AddWidget( center );
		_horizontalSplitter.SetStretch( 0, 0 );
		_horizontalSplitter.SetStretch( 1, 1 );
		_horizontalSplitter.SetCollapsible( 0, false );
		_horizontalSplitter.SetCollapsible( 1, false );

		_verticalSplitter.AddWidget( _horizontalSplitter );
		_verticalSplitter.AddWidget( timeline );
		_verticalSplitter.SetStretch( 0, 1 );
		_verticalSplitter.SetStretch( 1, 0 );
		_verticalSplitter.SetCollapsible( 0, false );
		_verticalSplitter.SetCollapsible( 1, false );

		_animationRightSplitter = new Splitter( _root )
		{
			IsVertical = true,
			OpaqueResize = true,
			HandleWidth = 4
		};
		_animationRightSplitter.AddWidget( inspector );
		_animationRightSplitter.AddWidget( clips );
		_animationRightSplitter.SetStretch( 0, 1 );
		_animationRightSplitter.SetStretch( 1, 1 );
		_animationRightSplitter.SetCollapsible( 0, false );
		_animationRightSplitter.SetCollapsible( 1, false );

		_animationOuterSplitter = new Splitter( _root )
		{
			IsHorizontal = true,
			OpaqueResize = true,
			HandleWidth = 4
		};
		_animationOuterSplitter.AddWidget( _verticalSplitter );
		_animationOuterSplitter.AddWidget( _animationRightSplitter );
		_animationOuterSplitter.SetStretch( 0, 1 );
		_animationOuterSplitter.SetStretch( 1, 0 );
		_animationOuterSplitter.SetCollapsible( 0, false );
		_animationOuterSplitter.SetCollapsible( 1, false );
		_root.Layout.Add( _animationOuterSplitter, 1 );

		var workspace = _controller.Document.Workspace;
		if ( !string.IsNullOrWhiteSpace( workspace.AnimationMainSplitterState ) )
			_horizontalSplitter.RestoreState( workspace.AnimationMainSplitterState );
		if ( !string.IsNullOrWhiteSpace( workspace.AnimationVerticalSplitterState ) )
			_verticalSplitter.RestoreState( workspace.AnimationVerticalSplitterState );
		if ( !string.IsNullOrWhiteSpace( workspace.AnimationRightSplitterState ) )
			_animationRightSplitter.RestoreState( workspace.AnimationRightSplitterState );
		if ( !string.IsNullOrWhiteSpace( workspace.AnimationOuterSplitterState ) )
			_animationOuterSplitter.RestoreState( workspace.AnimationOuterSplitterState );
	}

	private void BuildSplitLayout(
		Widget left,
		Widget center,
		Widget right,
		Widget bottom,
		bool calibration )
	{
		if ( _root is null )
			return;
		_horizontalSplitter = new Splitter( _root )
		{
			IsHorizontal = true,
			OpaqueResize = true,
			HandleWidth = 4
		};
		_horizontalSplitter.AddWidget( left );
		_horizontalSplitter.AddWidget( center );
		_horizontalSplitter.AddWidget( right );
		_horizontalSplitter.SetStretch( 0, 0 );
		_horizontalSplitter.SetStretch( 1, 1 );
		_horizontalSplitter.SetStretch( 2, 0 );
		_horizontalSplitter.SetCollapsible( 0, false );
		_horizontalSplitter.SetCollapsible( 1, false );
		_horizontalSplitter.SetCollapsible( 2, false );

		_verticalSplitter = new Splitter( _root )
		{
			IsVertical = true,
			OpaqueResize = true,
			HandleWidth = 4
		};
		_verticalSplitter.AddWidget( _horizontalSplitter );
		_verticalSplitter.AddWidget( bottom );
		_verticalSplitter.SetStretch( 0, 1 );
		_verticalSplitter.SetStretch( 1, 0 );
		_verticalSplitter.SetCollapsible( 0, false );
		_verticalSplitter.SetCollapsible( 1, false );
		_root.Layout.Add( _verticalSplitter, 1 );

		var workspace = _controller.Document.Workspace;
		var horizontalState = calibration
			? workspace.CalibrationSplitterState
			: workspace.AnimationSplitterState;
		var verticalState = calibration
			? workspace.CalibrationVerticalSplitterState
			: workspace.AnimationVerticalSplitterState;
		if ( !string.IsNullOrWhiteSpace( horizontalState ) )
			_horizontalSplitter.RestoreState( horizontalState );
		if ( !string.IsNullOrWhiteSpace( verticalState ) )
			_verticalSplitter.RestoreState( verticalState );
	}

	private void SaveWorkspaceState()
	{
		if ( _horizontalSplitter is null || _verticalSplitter is null )
			return;
		var workspace = _controller.Document.Workspace;
		if ( _controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate )
		{
			workspace.CalibrationSplitterState = _horizontalSplitter.SaveState();
			workspace.CalibrationVerticalSplitterState = _verticalSplitter.SaveState();
		}
		else
		{
			workspace.AnimationMainSplitterState = _horizontalSplitter.SaveState();
			workspace.AnimationVerticalSplitterState = _verticalSplitter.SaveState();
			if ( _animationRightSplitter is not null )
				workspace.AnimationRightSplitterState = _animationRightSplitter.SaveState();
			if ( _animationOuterSplitter is not null )
				workspace.AnimationOuterSplitterState = _animationOuterSplitter.SaveState();
		}
	}

	private void DestroyWorkspace()
	{
		if ( _root.IsValid() )
			_root!.Destroy();
		_root = null;
		_toolbar = null;
		_viewport = null;
		_statusPanel = null;
		_horizontalSplitter = null;
		_verticalSplitter = null;
		_animationRightSplitter = null;
		_animationOuterSplitter = null;
		_validationButton = null;
		_playButton = null;
	}

	private void ImportSource()
	{
		var dialog = new FileDialog( this )
		{
			Title = "Import Rigged Weapon",
			Directory = global::Editor.FileSystem.Content.GetFullPath( "/" )
		};
		dialog.SetModeOpen();
		dialog.SetFindExistingFile();
		dialog.SetNameFilter( "Rigged Models (*.fbx *.smd *.dmx *.vmdl)" );
		if ( !dialog.Execute() )
			return;

		SourceImportResult? import = null;
		PreviewHostResult? host = null;
		_controller.Mutate( "Import source weapon", document =>
		{
			import = _importer.Import( document, dialog.SelectedFile );
			if ( import.Success )
				host = PreviewHostBuilder.Build( document );
		} );

		_statusPanel?.SetMessage(
			$"{import?.Message} {host?.Message}",
			import?.Success == true && host?.Success == true
				? ValidationSeverity.Info
				: ValidationSeverity.Error );
		_viewport?.RebuildPreview();
	}

	private void ReimportSource()
	{
		var source = string.IsNullOrWhiteSpace( _controller.Document.Source.OriginalSourcePath )
			? _controller.Document.Source.SourcePath
			: _controller.Document.Source.OriginalSourcePath;
		if ( string.IsNullOrWhiteSpace( source ) )
		{
			ImportSource();
			return;
		}

		SourceImportResult? result = null;
		_controller.Mutate( "Reimport source weapon", document =>
		{
			result = _importer.Import( document, source );
			if ( result.Success )
				PreviewHostBuilder.Build( document );
		} );
		_statusPanel?.SetMessage(
			result?.Message ?? "Reimport failed.",
			result?.Success == true ? ValidationSeverity.Info : ValidationSeverity.Error );
		_viewport?.RebuildPreview();
	}

	private void AutoAlign()
	{
		var document = _controller.Document;
		var grip = document.Calibration.GetAnchor( AnchorKind.Grip );
		var rear = document.Calibration.GetAnchor( AnchorKind.RearBore );
		var front = document.Calibration.GetAnchor( AnchorKind.FrontBore );
		if ( grip is null || rear is null || front is null )
		{
			_statusPanel?.SetMessage(
				"Set the primary grip and both optional alignment markers before running Auto-align.",
				ValidationSeverity.Error );
			return;
		}

		if ( !WeaponAnimationMath.TryCalculateAlignment(
			grip.LocalPosition,
			rear.LocalPosition,
			front.LocalPosition,
			document.Calibration.UpAxis,
			document.Calibration.UniformScale,
			new Vector3( 12, -3, -2 ),
			out var alignment ) )
		{
			_statusPanel?.SetMessage( "The selected anchors cannot produce a finite alignment.", ValidationSeverity.Error );
			return;
		}

		_controller.Mutate( "Auto-align weapon", d =>
		{
			d.Calibration.PhysicalTransform = alignment.PhysicalTransform;
			var rearWorld = alignment.PhysicalTransform.PointToWorld( rear.LocalPosition );
			var correctionWorld = new Vector3( 0, -rearWorld.y, -rearWorld.z );
			var correctionLocal = alignment.PhysicalTransform.PointToLocal(
				alignment.PhysicalTransform.Position + correctionWorld );
			d.Calibration.FramingTransform = d.Calibration.FramingTransform.WithPosition( correctionLocal );
			d.Calibration.Confirmed = false;
		} );

		_statusPanel?.SetMessage(
			alignment.BoreMayBeReversed
				? "Aligned, but the bore points appear reversed. Swap rear and front if the muzzle faces away from +X."
				: "Grip placed at the canonical hand origin; bore aligned to +X and projected through the crosshair.",
			alignment.BoreMayBeReversed ? ValidationSeverity.Warning : ValidationSeverity.Info );
	}

	private void ConfirmCalibration()
	{
		PreviewHostResult? hostResult = null;
		_controller.Mutate( "Build calibrated preview host", document =>
			hostResult = PreviewHostBuilder.Build( document ) );
		var report = WeaponAnimationValidator.ValidateCalibration( _controller.Document );
		if ( !report.IsValid || hostResult?.Success != true )
		{
			_statusPanel?.SetReport( report, hostResult?.Message ?? "" );
			return;
		}

		var previous = _controller.Document.Calibration.Snapshot;
		_controller.Mutate( "Confirm calibration", document =>
		{
			if ( _rebaseOnConfirm && previous is not null )
				CalibrationRebaser.RebaseAnimationData( document, previous );

			var calibration = document.Calibration;
			calibration.Revision++;
			calibration.Confirmed = true;
			calibration.Snapshot = new CalibrationSnapshot
			{
				Revision = calibration.Revision,
				SourceHash = document.Source.SourceHash,
				RigHash = document.Rig.ProfileHash,
				UniformScale = calibration.UniformScale,
				PhysicalTransform = calibration.PhysicalTransform,
				FramingTransform = calibration.FramingTransform,
				Anchors = Json.Deserialize<System.Collections.Generic.List<WeaponAnchor>>(
					Json.Serialize( calibration.Anchors ) ) ?? [],
				ConfirmedUtc = DateTime.UtcNow
			};
			if ( previous is null )
				CalibrationBindingSeeder.SeedDefaultPrimaryHand( document );

			var idle = document.EnsureClip( WeaponClipRole.Idle );
			if ( idle.Tracks.Count == 0 || idle.IsBindPoseSeed )
			{
				var skeleton = HostSkeletonBuilder.Build( document );
				IdleBindPoseService.SeedFromCurrentBind( document, skeleton );
			}
			idle.Readiness = ClipReadiness.Ready;
			document.Workspace.SelectedClipId = idle.Id;
			document.ActiveStage = WeaponAnimatorStage.Animate;
		} );
		_rebaseOnConfirm = false;
		BuildWorkspace();
	}

	private void RequestCalibrationStage()
	{
		if ( _controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate )
			return;
		var hasAnimation = _controller.Document.Clips.Any( x =>
			x.Tracks.Count > 0 && x.Role != WeaponClipRole.Idle );
		if ( !hasAnimation )
		{
			SwitchStage( WeaponAnimatorStage.Calibrate );
			return;
		}

		Dialog.AskConfirm(
			() =>
			{
				_rebaseOnConfirm = true;
				SwitchStage( WeaponAnimatorStage.Calibrate );
			},
			() =>
			{
				Dialog.AskConfirm(
					() =>
					{
						_controller.Mutate(
							"Discard animation for recalibration",
							CalibrationRebaser.DiscardAnimationData );
						_rebaseOnConfirm = false;
						SwitchStage( WeaponAnimatorStage.Calibrate );
					},
					"Discard all authored animation and binding data before recalibrating?",
					"Discard Animation Data",
					"Discard",
					"Cancel" );
			},
			"Rebase bindings, controls, and animation roots onto the new calibration when it is confirmed?",
			"Return to Calibration",
			"Rebase",
			"Other Options" );
	}

	private void SwitchStage( WeaponAnimatorStage stage )
	{
		if ( stage == _controller.Document.ActiveStage )
			return;
		if ( stage == WeaponAnimatorStage.Animate )
		{
			var report = WeaponAnimationValidator.ValidateCalibration( _controller.Document );
			if ( !_controller.Document.Calibration.Confirmed || !report.IsValid )
			{
				_statusPanel?.SetMessage(
					"Confirm a valid calibration before entering Animate.",
					ValidationSeverity.Error );
				return;
			}
		}

		SaveWorkspaceState();
		_controller.Mutate( $"Switch to {stage}", d => d.ActiveStage = stage );
		BuildWorkspace();
	}

	private bool Save()
	{
		if ( _asset is null || _resource is null )
		{
			SaveAs();
			return _asset is not null;
		}

		SaveWorkspaceState();
		if ( _migrationBackupRequired )
		{
			try
			{
				WeaponAnimationMigration.CreateBackup(
					_asset.AbsolutePath,
					_migration?.SourceSchemaVersion ?? 2 );
			}
			catch ( Exception ex )
			{
				_statusPanel?.SetMessage(
					$"Migration backup failed; the project was not saved: {ex.Message}",
					ValidationSeverity.Error );
				return false;
			}
		}

		_resource.Document = _controller.Document;
		if ( !_asset.SaveToDisk( _resource ) )
		{
			_statusPanel?.SetMessage( "The .wepanim asset could not be saved.", ValidationSeverity.Error );
			return false;
		}

		_controller.MarkSaved();
		_migrationBackupRequired = false;
		RecoveryService.Clear( _controller.Document.DocumentId );
		_statusPanel?.SetMessage( $"Saved {_asset.Path}." );
		RefreshTitle();
		return true;
	}

	private void SaveAs()
	{
		var dialog = new FileDialog( this )
		{
			Title = "Save Weapon Animation Project As",
			Directory = global::Editor.FileSystem.Content.GetFullPath( "/" ),
			DefaultSuffix = "wepanim"
		};
		dialog.SetModeSave();
		dialog.SetFindFile();
		dialog.SetNameFilter( "Weapon Animation Project (*.wepanim)" );
		if ( !dialog.Execute() )
			return;

		var path = Path.ChangeExtension( dialog.SelectedFile, ".wepanim" );
		var asset = AssetSystem.CreateResource( "wepanim", path );
		if ( asset is null )
		{
			_statusPanel?.SetMessage( "Could not create the new .wepanim asset.", ValidationSeverity.Error );
			return;
		}

		_asset = asset;
		_resource = new WeaponAnimationAsset { Document = _controller.Document };
		if ( !_asset.SaveToDisk( _resource ) )
		{
			_statusPanel?.SetMessage( "Save As failed.", ValidationSeverity.Error );
			return;
		}
		_controller.MarkSaved();
		_migrationBackupRequired = false;
		RecoveryService.Clear( _controller.Document.DocumentId );
		RefreshTitle();
	}

	private void GenerateAssets()
	{
		var result = _generator.Generate( _controller.Document );
		if ( result.Success )
		{
			_statusPanel?.SetMessage(
				$"Generated and reloaded {result.GeneratedFiles.Count} files in {result.OutputFolder}.",
				ValidationSeverity.Info );
			Save();
		}
		else
		{
			var message = string.Join(
				"  ·  ",
				result.Diagnostics.Where( x => x.Severity == ValidationSeverity.Error )
					.Select( x => x.Message )
					.Take( 4 ) );
			_statusPanel?.SetMessage(
				string.IsNullOrWhiteSpace( message ) ? "Generation failed validation." : message,
				ValidationSeverity.Error );
		}
		RefreshToolbarState();
	}

	private void Validate()
	{
		var report = _controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate
			? WeaponAnimationValidator.ValidateCalibration( _controller.Document )
			: WeaponAnimationValidator.ValidateForGeneration( _controller.Document );
		_statusPanel?.SetReport( report );
		RefreshToolbarState();
	}

	private void RebuildPreviewHost()
	{
		PreviewHostResult? result = null;
		_controller.Mutate( "Rebuild preview host", document =>
			result = PreviewHostBuilder.Build( document ) );
		_statusPanel?.SetMessage(
			result?.Message ?? "Preview host rebuild failed.",
			result?.Success == true ? ValidationSeverity.Info : ValidationSeverity.Error );
		_viewport?.RebuildPreview();
	}

	private void OpenGeneratedFolder()
	{
		var path = AssetGenerationService.GetOutputFolder( _controller.Document );
		if ( Directory.Exists( path ) )
			EditorUtility.OpenFolder( path );
		else
			_statusPanel?.SetMessage( "Generate assets before opening the output folder.", ValidationSeverity.Warning );
	}

	private void TogglePlayback()
	{
		_controller.TogglePlayback();
	}

	private void ResetWorkspace()
	{
		_controller.Mutate( "Reset workspace", document =>
		{
			var state = document.Workspace;
			state.CameraFocus = Vector3.Zero;
			state.CameraAngles = new Angles( 12, 180, 0 );
			state.CameraDistance = 48;
			state.FreeLookCamera = false;
			state.CameraPosition = Vector3.Zero;
			state.CameraMoveSpeed = 1;
			state.FullBrightViewport = false;
			state.CalibrationSplitterState = "";
			state.CalibrationVerticalSplitterState = "";
			state.AnimationSplitterState = "";
			state.AnimationVerticalSplitterState = "";
			state.AnimationTimelineSplitterState = "";
			state.AnimationRightSplitterState = "";
			state.AnimationMainSplitterState = "";
			state.AnimationOuterSplitterState = "";
			state.TimelineViews.Clear();
		} );
		BuildWorkspace();
		_viewport?.FitCamera();
	}

	private void OpenPreferences()
	{
		new WeaponAnimatorPreferencesWindow( _controller ).Show();
	}

	private void OnDocumentChanged()
	{
		if ( _controller.IsDirty )
			RecoveryService.Write( _controller.Document );
		RefreshTitle();
		RefreshToolbarState();
	}

	private void OnDirtyChanged()
	{
		if ( _controller.IsDirty )
			RecoveryService.Write( _controller.Document );
		RefreshTitle();
	}

	private void RefreshTitle()
	{
		var name = _controller.Document.Name;
		var dirty = _controller.IsDirty ? " *" : "";
		WindowTitle = $"S&box Weapon Animator — {name}{dirty}";
		Title = WindowTitle;
	}

	private void RefreshToolbarState()
	{
		if ( _validationButton is null )
			return;
		var report = _controller.Document.ActiveStage == WeaponAnimatorStage.Calibrate
			? WeaponAnimationValidator.ValidateCalibration( _controller.Document )
			: WeaponAnimationValidator.ValidateForGeneration( _controller.Document );
		_validationButton.Text = report.IsValid
			? report.WarningCount > 0 ? $"{report.WarningCount} warnings" : "Valid"
			: $"{report.ErrorCount} errors";
		if ( _validationButton is WeaponAnimatorButton validationButton )
			validationButton.FitToContent( true );
		_validationButton.Icon = report.IsValid
			? report.WarningCount > 0 ? "warning" : "check_circle"
			: "error";
		_validationButton.Tint = report.IsValid
			? report.WarningCount > 0 ? WeaponAnimatorTheme.Amber * 0.45f : WeaponAnimatorTheme.Green * 0.45f
			: WeaponAnimatorTheme.Coral * 0.5f;
		if ( _playButton is not null )
		{
			_playButton.Text = _controller.IsPlaying ? "Pause" : "Play";
			_playButton.Icon = _controller.IsPlaying ? "pause" : "play_arrow";
			if ( _playButton is WeaponAnimatorButton playButton )
				playButton.FitToContent( true );
		}
		_toolbar?.BalanceCenter();
	}

	private bool OfferRecovery()
	{
		if ( _asset is null )
			return false;
		var writeUtc = File.Exists( _asset.AbsolutePath )
			? File.GetLastWriteTimeUtc( _asset.AbsolutePath )
			: DateTime.MinValue;
		var recovery = RecoveryService.ReadNewerThan( _controller.Document.DocumentId, writeUtc );
		if ( recovery is null )
			return false;

		Dialog.AskConfirm(
			() =>
			{
				var migration = MigrateAndRepair( recovery );
				if ( migration.Migrated )
				{
					_migration = migration;
					_migrationBackupRequired = true;
				}
				NormalizeRecoveredSource( recovery );
				if ( recovery.Source.Compiled )
					PreviewHostBuilder.Build( recovery );
				_controller.ReplaceWithoutHistory( recovery, true );
				BuildWorkspace();
				var message = migration.Migrated
					? $"Recovered the newer autosave snapshot. {migration.Summary}"
					: "Recovered the newer autosave snapshot.";
				_statusPanel?.SetMessage( message, ValidationSeverity.Warning );
			},
			() => RecoveryService.Clear( _controller.Document.DocumentId ),
			"A newer recovery snapshot exists for this project. Restore it?",
			"Recover Weapon Animation Project",
			"Restore",
			"Discard Recovery" );
		return true;
	}

	private void OfferCachedImportRecovery()
	{
		var document = _controller.Document;
		if ( !string.IsNullOrWhiteSpace( document.Source.SourcePath ) )
			return;

		var cachedSource = FindCachedSource( document.DocumentId );
		if ( string.IsNullOrWhiteSpace( cachedSource ) )
			return;

		Dialog.AskConfirm(
			() =>
			{
				var result = _importer.Import( document, cachedSource );
				var host = result.Success ? PreviewHostBuilder.Build( document ) : null;
				_controller.ReplaceWithoutHistory( document, true );
				BuildWorkspace();
				_statusPanel?.SetMessage(
					$"{result.Message} {host?.Message}",
					result.Success && host?.Success == true
						? ValidationSeverity.Info
						: ValidationSeverity.Error );
			},
			() => { },
			"The saved document is empty, but a previous weapon import remains in its private cache. Recover that import?",
			"Recover Cached Weapon Import",
			"Recover Import",
			"Ignore Cache" );
	}

	private static string FindCachedSource( Guid documentId )
	{
		var cache = WeaponSourceImporter.GetPreviewCacheRoot( documentId );
		if ( !Directory.Exists( cache ) )
			return "";

		var wrapper = Directory.EnumerateFiles( cache, "source_*.vmdl" )
			.OrderByDescending( File.GetLastWriteTimeUtc )
			.FirstOrDefault();
		if ( string.IsNullOrWhiteSpace( wrapper ) )
			return "";

		var match = Regex.Match(
			File.ReadAllText( wrapper ),
			"filename\\s*=\\s*\"(?<path>[^\"]+\\.(?:fbx|smd|dmx|vmdl))\"",
			RegexOptions.IgnoreCase );
		if ( !match.Success )
			return "";

		var relative = match.Groups["path"].Value;
		var absolute = global::Editor.FileSystem.Content.GetFullPath( relative );
		if ( File.Exists( absolute ) )
			return absolute;

		var directory = Path.GetDirectoryName( absolute );
		var filename = Path.GetFileName( absolute );
		if ( string.IsNullOrWhiteSpace( directory ) || !Directory.Exists( directory ) )
			return "";

		return Directory.EnumerateFiles( directory )
			.FirstOrDefault( path =>
				Path.GetFileName( path ).Equals( filename, StringComparison.OrdinalIgnoreCase ) )
			?? "";
	}

	private void NormalizeRecoveredSource( WeaponAnimationDocument document )
	{
		if ( !document.Source.Compiled
			|| !document.Source.NeedsModelDocWrapper
			|| string.IsNullOrWhiteSpace( document.Rig.RootBone )
			|| document.Rig.RootBone.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase )
			|| !string.IsNullOrWhiteSpace( document.Source.SourceRootBoneName ) )
			return;

		var source = string.IsNullOrWhiteSpace( document.Source.OriginalSourcePath )
			? document.Source.SourcePath
			: document.Source.OriginalSourcePath;
		_importer.Import( document, source );
	}

	private void CloseAfterPrompt()
	{
		_allowClose = true;
		Close();
	}

	private static WeaponAnimationMigrationResult MigrateAndRepair(
		WeaponAnimationDocument document ) =>
		WeaponAnimationMigration.MigrateAndRepair( document );
}

public static class WeaponAnimatorLauncher
{
	[Menu( "Editor", "Tools/Weapon Animator/Open Weapon Animator", "animation", Priority = 0 )]
	public static void OpenPicker()
	{
		new WeaponAnimatorPickerWindow().Show();
	}

	public static void CreateNew()
	{
		var dialog = new FileDialog( null )
		{
			Title = "Create Weapon Animation Project",
			Directory = global::Editor.FileSystem.Content.GetFullPath( "/" ),
			DefaultSuffix = "wepanim"
		};
		dialog.SetModeSave();
		dialog.SetFindFile();
		dialog.SetNameFilter( "Weapon Animation Project (*.wepanim)" );
		if ( !dialog.Execute() )
			return;

		var path = Path.ChangeExtension( dialog.SelectedFile, ".wepanim" );
		var asset = AssetSystem.CreateResource( "wepanim", path );
		if ( asset is null )
			return;
		var resource = new WeaponAnimationAsset
		{
			Document = WeaponAnimationDocument.CreateDefault( Path.GetFileNameWithoutExtension( path ) )
		};
		asset.SaveToDisk( resource );
		IAssetEditor.OpenInEditor( asset, out _ );
	}

	public static void OpenExisting()
	{
		var dialog = new FileDialog( null )
		{
			Title = "Open Weapon Animation Project",
			Directory = global::Editor.FileSystem.Content.GetFullPath( "/" )
		};
		dialog.SetModeOpen();
		dialog.SetFindExistingFile();
		dialog.SetNameFilter( "Weapon Animation Project (*.wepanim)" );
		if ( !dialog.Execute() )
			return;

		var asset = AssetSystem.FindByPath( dialog.SelectedFile )
			?? AssetSystem.RegisterFile( dialog.SelectedFile );
		if ( asset is not null )
			IAssetEditor.OpenInEditor( asset, out _ );
	}
}

internal sealed class WeaponAnimatorPickerWindow : Window
{
	public WeaponAnimatorPickerWindow()
	{
		DeleteOnClose = true;
		WindowTitle = "Weapon Animator";
		Title = WindowTitle;
		Size = new Vector2( 520, 260 );
		SetWindowIcon( "animation" );

		var root = new Widget( this );
		root.SetStyles( "background-color: rgb(13,15,17);" );
		root.Layout = Layout.Column();
		root.Layout.Margin = new Sandbox.UI.Margin( 28 );
		root.Layout.Spacing = 14;
		var title = WeaponAnimatorTheme.Label( "WEAPON ANIMATOR", root );
		title.SetStyles(
			"background-color: transparent; border: none; padding: 0px;" +
			$"font-size: 18px; font-weight: 600; letter-spacing: 1.2px; color: {WeaponAnimatorTheme.Text.Hex};" );
		root.Layout.Add( title );
		var description = WeaponAnimatorTheme.Label(
			"Open a document-driven import, calibration, binding, and animation workspace. No active scene or selected GameObject is required.",
			root,
			true );
		description.WordWrap = true;
		root.Layout.Add( description );
		var row = RigAuditPanel.Row( root );
		row.Layout.Add( WeaponAnimatorTheme.Button(
			"New project",
			"note_add",
			() =>
			{
				Close();
				WeaponAnimatorLauncher.CreateNew();
			},
			row,
			true ), 1 );
		row.Layout.Add( WeaponAnimatorTheme.Button(
			"Open existing",
			"folder_open",
			() =>
			{
				Close();
				WeaponAnimatorLauncher.OpenExisting();
			},
			row ), 1 );
		root.Layout.Add( row );
		root.Layout.AddStretchCell();
		Canvas = root;
	}
}

internal sealed class WeaponAnimatorPreferencesWindow : Window
{
	public WeaponAnimatorPreferencesWindow( WeaponAnimatorController controller )
	{
		DeleteOnClose = true;
		WindowTitle = "Weapon Animator Preferences";
		Title = WindowTitle;
		Size = new Vector2( 420, 520 );
		var root = new Widget( this );
		root.SetStyles( "background-color: rgb(13,15,17);" );
		root.Layout = Layout.Column();
		root.Layout.Margin = new Sandbox.UI.Margin( 18 );
		root.Layout.Spacing = 8;
		root.Layout.Add( Toggle(
			"Auto-key transformed controls",
			controller.Document.Workspace.AutoKey,
			value => controller.Mutate( "Auto-key preference", d => d.Workspace.AutoKey = value ) ) );
		root.Layout.Add( Toggle(
			"Use local gizmo space",
			controller.Document.Workspace.LocalGizmos,
			value => controller.Mutate( "Gizmo preference", d => d.Workspace.LocalGizmos = value ) ) );
		root.Layout.Add( Toggle(
			"Snap position",
			controller.Document.Workspace.SnapPosition,
			value => controller.Mutate( "Position snapping", d => d.Workspace.SnapPosition = value ) ) );
		root.Layout.Add( Toggle(
			"Snap rotation",
			controller.Document.Workspace.SnapRotation,
			value => controller.Mutate( "Rotation snapping", d => d.Workspace.SnapRotation = value ) ) );
		root.Layout.Add( Number(
			"Rotation snap angle",
			controller.Document.Workspace.RotationSnapDegrees,
			0.25f,
			180,
			value => controller.UpdateWorkspacePreference(
				"Rotation snap angle",
				workspace => workspace.RotationSnapDegrees = value ) ) );
		root.Layout.Add( WeaponAnimatorTheme.SectionLabel(
			"VIEWPORT GRID",
			root,
			topMargin: true ) );
		root.Layout.Add( Number(
			"Grid opacity",
			controller.Document.Workspace.GridOpacity,
			0,
			0.5f,
			value => controller.UpdateWorkspacePreference(
				"Grid opacity",
				workspace => workspace.GridOpacity = value ) ) );
		root.Layout.Add( Number(
			"Grid line weight",
			controller.Document.Workspace.GridLineThickness,
			0.1f,
			2,
			value => controller.UpdateWorkspacePreference(
				"Grid line weight",
				workspace => workspace.GridLineThickness = value ) ) );
		root.Layout.Add( WeaponAnimatorTheme.SectionLabel(
			"VIEWPORT LIGHTING",
			root,
			topMargin: true ) );
		root.Layout.Add( Toggle(
			"Cyan edge light",
			controller.Document.Workspace.RimLightEnabled,
			value => controller.UpdateWorkspacePreference(
				"Cyan edge light",
				workspace => workspace.RimLightEnabled = value ) ) );
		root.Layout.Add( Number(
			"Cyan edge brightness",
			controller.Document.Workspace.RimLightIntensity,
			0,
			12,
			value => controller.UpdateWorkspacePreference(
				"Cyan edge brightness",
				workspace => workspace.RimLightIntensity = value ) ) );
		var lightingNote = WeaponAnimatorTheme.Label(
			"The edge light is disabled automatically in Full Bright.",
			root,
			true );
		lightingNote.WordWrap = true;
		root.Layout.Add( lightingNote );
		root.Layout.AddStretchCell();
		root.Layout.Add( WeaponAnimatorTheme.Button( "Close", "close", Close, root, true ) );
		Canvas = root;

		Button Toggle( string text, bool value, Action<bool> changed )
		{
			var button = new WeaponAnimatorButton( text, root )
			{
				IsToggle = true,
				IsChecked = value,
				Tint = WeaponAnimatorTheme.SurfaceRaised
			};
			button.Toggled = () => changed( button.IsChecked );
			return button;
		}

		Widget Number(
			string text,
			float value,
			float minimum,
			float maximum,
			Action<float> changed )
		{
			var container = new Widget( root );
			container.Layout = Layout.Column();
			container.Layout.Margin = 0;
			container.Layout.Spacing = 3;
			var row = RigAuditPanel.Row( container );
			row.Layout.Add( WeaponAnimatorTheme.Label( text, row, true ), 1 );
			var edit = new LineEdit( row )
			{
				Text = value.ToString( "0.##", CultureInfo.InvariantCulture ),
				FixedWidth = 82,
				FixedHeight = 27
			};
			edit.SetStyles( WeaponAnimatorTheme.InputStyle );
			var slider = new FloatSlider( container )
			{
				Minimum = minimum,
				Maximum = maximum,
				Value = value,
				FixedHeight = 18
			};

			void Apply( float candidate, bool updateEdit )
			{
				var clamped = Math.Clamp( candidate, minimum, maximum );
				if ( updateEdit )
					edit.Text = clamped.ToString( "0.##", CultureInfo.InvariantCulture );
				slider.Value = clamped;
				changed( clamped );
			}

			edit.TextEdited += textValue =>
			{
				if ( float.TryParse(
					textValue,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var parsed )
					&& WeaponAnimationMath.IsFinite( parsed ) )
					Apply( parsed, false );
			};
			edit.EditingFinished += () => Apply( slider.Value, true );
			slider.OnValueEdited = () => Apply( slider.Value, true );
			row.Layout.Add( edit );
			container.Layout.Add( row );
			container.Layout.Add( slider );
			return container;
		}
	}
}
