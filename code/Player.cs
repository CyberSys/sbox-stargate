﻿using Sandbox;
using System;
using System.Linq;

partial class SandboxPlayer : Player
{
	private TimeSince timeSinceDropped;
	private TimeSince timeSinceJumpReleased;

	private DamageInfo lastDamage;

	[Net, Predicted]
	public bool ThirdPersonCamera { get; set; }

	/// <summary>
	/// The clothing container is what dresses the citizen
	/// </summary>
	public ClothingContainer Clothing = new();

	/// <summary>
	/// Default init
	/// </summary>
	public SandboxPlayer()
	{
		Inventory = new Inventory( this );
	}

	/// <summary>
	/// Initialize using this client
	/// </summary>
	public SandboxPlayer( IClient cl ) : this()
	{
		// Load clothing from client data
		Clothing.LoadFromClient( cl );
	}

	public override void Respawn()
	{
		SetModel( "models/citizen/citizen.vmdl" );

		Controller = new PlayerWalkController();

		if ( DevController is NoclipController )
		{
			DevController = null;
		}

		EnableAllCollisions = true;
		EnableDrawing = true;
		EnableHideInFirstPerson = true;
		EnableShadowInFirstPerson = true;

		Clothing.DressEntity( this );

		Inventory.Add( new PhysGun(), true );
		Inventory.Add( new GravGun() );
		Inventory.Add( new Tool() );
		Inventory.Add( new Pistol() );
		Inventory.Add( new Flashlight() );
		Inventory.Add( new Fists() );

		//base.Respawn();

		Game.AssertServer();

		LifeState = LifeState.Alive;
		Health = 100;
		Velocity = Vector3.Zero;

		CreateHull();

		//GameManager.Current?.MoveToSpawnpoint( this );

		var spawnpoint = All
		.OfType<SpawnPoint>()               // get all SpawnPoint entities
		.OrderBy( x => Guid.NewGuid() )     // order them by random
		.FirstOrDefault();                  // take the first one

		if ( spawnpoint == null )
		{
			Log.Warning( $"Couldn't find spawnpoint for {this}!" );
			return;
		}

		Transform = spawnpoint.Transform;
		SetPlayerViewAngles( spawnpoint.Rotation.Angles() );

		ResetInterpolation();
	}

	[ClientRpc]
	public void SetPlayerViewAngles( Angles ang )
	{
		ViewAngles = ang;
	}

	public override void OnKilled()
	{
		base.OnKilled();

		if ( lastDamage.HasTag( "vehicle" ) )
		{
			Particles.Create( "particles/impact.flesh.bloodpuff-big.vpcf", lastDamage.Position );
			Particles.Create( "particles/impact.flesh-big.vpcf", lastDamage.Position );
			PlaySound( "kersplat" );
		}

		BecomeRagdollOnClient( Velocity, lastDamage.Position, lastDamage.Force, lastDamage.BoneIndex, lastDamage.HasTag( "bullet" ), lastDamage.HasTag( "blast" ) );

		Controller = null;

		EnableAllCollisions = false;
		EnableDrawing = false;

		foreach ( var child in Children )
		{
			child.EnableDrawing = false;
		}

		Inventory.DropActive();
		Inventory.DeleteContents();
	}

	public override void TakeDamage( DamageInfo info )
	{
		if ( info.Hitbox.HasTag("head") )
		{
			info.Damage *= 10.0f;
		}

		lastDamage = info;

		base.TakeDamage( info );
	}

	public override PawnController GetActiveController()
	{
		if ( DevController != null ) return DevController;

		return base.GetActiveController();
	}

	public override void Simulate( IClient cl )
	{
		base.Simulate( cl );

		if ( LifeState != LifeState.Alive )
			return;

		var controller = GetActiveController();
		if ( controller != null )
		{
			EnableSolidCollisions = !controller.HasTag( "noclip" );

			SimulateAnimation( controller );
		}

		TickPlayerUse();
		SimulateActiveChild( cl, ActiveChild );

		if ( Input.Pressed( InputButton.View ) )
		{
			ThirdPersonCamera = !ThirdPersonCamera;
		}

		if ( Input.Pressed( InputButton.Drop ) )
		{
			var dropped = Inventory.DropActive();
			if ( dropped != null )
			{
				dropped.PhysicsGroup.ApplyImpulse( Velocity + EyeRotation.Forward * 500.0f + Vector3.Up * 100.0f, true );
				dropped.PhysicsGroup.ApplyAngularImpulse( Vector3.Random * 100.0f, true );

				timeSinceDropped = 0;
			}
		}

		if ( Input.Released( InputButton.Jump ) )
		{
			if ( timeSinceJumpReleased < 0.3f )
			{
				if ( DevController is NoclipController )
				{
					DevController = null;
				}
				else
				{
					DevController = new NoclipController();
				}
			}

			timeSinceJumpReleased = 0;
		}

		if ( InputDirection.y != 0 || InputDirection.x != 0 )
		{
			timeSinceJumpReleased = 1;
		}
	}

	Entity lastWeapon;

	void SimulateAnimation( PawnController controller )
	{
		if ( controller == null )
			return;

		// where should we be rotated to
		var turnSpeed = 0.02f;

		Rotation rotation;

		// If we're a bot, spin us around 180 degrees.
		if ( Client.IsBot )
			rotation = ViewAngles.WithYaw( ViewAngles.yaw + 180f ).ToRotation();
		else
			rotation = ViewAngles.ToRotation();

		var idealRotation = Rotation.LookAt( rotation.Forward.WithZ( 0 ), Vector3.Up );
		Rotation = Rotation.Slerp( Rotation, idealRotation, controller.WishVelocity.Length * Time.Delta * turnSpeed );
		Rotation = Rotation.Clamp( idealRotation, 45.0f, out var shuffle ); // lock facing to within 45 degrees of look direction

		CitizenAnimationHelper animHelper = new CitizenAnimationHelper( this );

		animHelper.WithWishVelocity( controller.WishVelocity );
		animHelper.WithVelocity( controller.Velocity );
		animHelper.WithLookAt( EyePosition + EyeRotation.Forward * 100.0f, 1.0f, 1.0f, 0.5f );
		animHelper.AimAngle = rotation;
		animHelper.FootShuffle = shuffle;
		animHelper.DuckLevel = MathX.Lerp( animHelper.DuckLevel, controller.HasTag( "ducked" ) ? 1 : 0, Time.Delta * 10.0f );
		animHelper.VoiceLevel = ( Game.IsClient && Client.IsValid() ) ? Client.Voice.LastHeard < 0.5f ? Client.Voice.CurrentLevel : 0.0f : 0.0f;
		animHelper.IsGrounded = GroundEntity != null;
		animHelper.IsSitting = controller.HasTag( "sitting" );
		animHelper.IsNoclipping = controller.HasTag( "noclip" );
		animHelper.IsClimbing = controller.HasTag( "climbing" );
		animHelper.IsSwimming = this.GetWaterLevel() >= 0.5f;
		animHelper.IsWeaponLowered = false;

		if ( controller.HasEvent( "jump" ) ) animHelper.TriggerJump();
		if ( ActiveChild != lastWeapon ) animHelper.TriggerDeploy();

		if ( ActiveChild is BaseCarriable carry )
		{
			carry.SimulateAnimator( animHelper );
		}
		else
		{
			animHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
			animHelper.AimBodyWeight = 0.5f;
		}

		lastWeapon = ActiveChild;
	}

	public override void StartTouch( Entity other )
	{
		if ( timeSinceDropped < 1 ) return;

		base.StartTouch( other );
	}

	TimeSince timeSinceLastFootstep = 0;
	public override void OnAnimEventFootstep( Vector3 pos, int foot, float volume )
	{
		if ( LifeState != LifeState.Alive )
			return;

		if ( !Game.IsClient )
			return;

		if ( timeSinceLastFootstep < 0.2f )
			return;

		volume *= FootstepVolume();

		timeSinceLastFootstep = 0;

		var trace = Trace.Ray( pos, pos + Vector3.Down * 20 )
			.Radius( 1 )
			.Ignore( this );

		trace = Stargate.GetAdjustedTraceForClipping( this, trace );
		
		var tr = trace.Run();

		if ( !tr.Hit ) return;

		tr.Surface.DoFootstep( this, tr, foot, volume );
	}

	public override float FootstepVolume()
	{
		return Velocity.WithZ( 0 ).Length.LerpInverse( 0.0f, 200.0f ) * 5.0f;
	}

	public override void FrameSimulate( IClient cl )
	{
		Camera.ZNear = 1f;
		Camera.ZFar = 64000f;
		Camera.Rotation = ViewAngles.ToRotation();
		Camera.FieldOfView = Screen.CreateVerticalFieldOfView( Game.Preferences.FieldOfView );

		if ( ThirdPersonCamera )
		{
			Camera.FieldOfView = Screen.CreateVerticalFieldOfView( Game.Preferences.FieldOfView );
			Camera.FirstPersonViewer = null;

			Vector3 targetPos;
			var center = Position + Vector3.Up * 64;

			var pos = center;
			var rot = Camera.Rotation * Rotation.FromAxis( Vector3.Up, -16 );

			float distance = 130.0f * Scale;
			targetPos = pos + rot.Right * ((CollisionBounds.Mins.x + 32) * Scale);
			targetPos += rot.Forward * -distance;

			var tr = Trace.Ray( pos, targetPos )
				.WithAnyTags( "solid" )
				.Ignore( this )
				.Radius( 8 )
				.Run();

			Camera.Position = tr.EndPosition;
		}
		else if ( LifeState != LifeState.Alive && Corpse.IsValid() )
		{
			Corpse.EnableDrawing = true;

			var pos = Corpse.GetBoneTransform( 0 ).Position + Vector3.Up * 10;
			var targetPos = pos + Camera.Rotation.Backward * 100;

			var tr = Trace.Ray( pos, targetPos )
				.WithAnyTags( "solid" )
				.Ignore( this )
				.Radius( 8 )
				.Run();

			Camera.Position = tr.EndPosition;
			Camera.FirstPersonViewer = null;
		}
		else
		{
			Camera.Position = EyePosition;
			Camera.FieldOfView = Screen.CreateVerticalFieldOfView( Game.Preferences.FieldOfView );
			Camera.FirstPersonViewer = this;
			Camera.Main.SetViewModelCamera( Camera.FieldOfView );
		}
	}

	[ConCmd.Admin( "noclip" )]
	static void DoPlayerNoclip()
	{
		if ( ConsoleSystem.Caller.Pawn is SandboxPlayer basePlayer )
		{
			if ( basePlayer.DevController is NoclipController )
			{
				basePlayer.DevController = null;
			}
			else
			{
				basePlayer.DevController = new NoclipController();
			}
		}
	}

	[ConCmd.Admin( "kill" )]
	static void DoPlayerSuicide()
	{
		if ( ConsoleSystem.Caller.Pawn is SandboxPlayer basePlayer )
		{
			basePlayer.TakeDamage( new DamageInfo { Damage = basePlayer.Health * 99 } );
		}
	}
}
