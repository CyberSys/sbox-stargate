﻿﻿using Sandbox;

[Spawnable]
[Library( "weapon_pistol", Title = "Pistol" )]
partial class Pistol : Weapon
{
	public override float ReloadTime => 1.5f;
	public override float PrimaryRate => 15.0f;
	public override float SecondaryRate => 1.0f;

	public TimeSince TimeSinceDischarge { get; set; }

	public AnimatedEntity ViewModelArms { get; set; }

	Vector3 ViewModelOffset = Vector3.Zero;
	Vector3 AimPos => new Vector3( 1, -4.75f, 1.45f );

	public override void Spawn()
	{
		base.Spawn();

		Model = Cloud.Model( "https://asset.party/facepunch/w_usp" );
		SetBodyGroup( "barrel", 1 );
		//SetBodyGroup( "sights", 1 );
		LocalScale = 2f; // todo - this doesn't work when bone merged! we should make it multiply!
	}
	public override void CreateViewModel()
	{
		ViewModelEntity = new ViewModel();
		ViewModelEntity.Position = Position;
		ViewModelEntity.Owner = Owner;
		ViewModelEntity.EnableViewmodelRendering = true;
		ViewModelEntity.Model = Cloud.Model( "https://asset.party/facepunch/v_usp" );
		ViewModelEntity.SetBodyGroup( "barrel", 1 );
		//ViewModelEntity.SetBodyGroup( "sights", 0 );

		ViewModelArms = new AnimatedEntity( "models/first_person/first_person_arms.vmdl" );
		ViewModelArms.SetParent( ViewModelEntity, true );
		ViewModelArms.EnableViewmodelRendering = true;
	}
	public override void ActiveStart( Entity ent )
	{
		base.ActiveStart( ent );

		ViewModelEntity?.SetAnimParameter( "b_deploy", true );
	}

	public override void Reload()
	{
		base.Reload();

		ViewModelEntity?.SetAnimParameter( "b_reload", true );
	}

	public override bool CanPrimaryAttack()
	{
		return base.CanPrimaryAttack() && Input.Pressed( InputButton.PrimaryAttack );
	}

	public override void AttackPrimary()
	{
		TimeSincePrimaryAttack = 0;
		TimeSinceSecondaryAttack = 0;

		(Owner as AnimatedEntity)?.SetAnimParameter( "b_attack", true );
		ViewModelEntity?.SetAnimParameter( "b_attack", true );

		ShootEffects();
		PlaySound( "rust_pistol.shoot" );
		ShootBullet( 0.01f, 1.5f, 9.0f, 3.0f );
	}


	[ClientRpc]
	protected override void ShootEffects()
	{
		Game.AssertClient();

		Particles.Create( "particles/pistol_muzzleflash.vpcf", EffectEntity, "muzzle" );
		var shell = Particles.Create( "particles/sbox_stargate/shell_eject.vpcf", EffectEntity, "eject" );
		shell.Set( "shellsize", 0.5f );

		ViewModelEntity?.SetAnimParameter( "fire", true );
	}

	private void Discharge()
	{
		if ( TimeSinceDischarge < 0.5f )
			return;

		TimeSinceDischarge = 0;

		var muzzle = GetAttachment( "muzzle" ) ?? default;
		var pos = muzzle.Position;
		var rot = muzzle.Rotation;

		ShootEffects();
		PlaySound( "rust_pistol.shoot" );
		ShootBullet( pos, rot.Forward, 0.05f, 1.5f, 9.0f, 3.0f );

		ApplyAbsoluteImpulse( rot.Backward * 200.0f );
	}
	
	protected override void OnPhysicsCollision( CollisionEventData eventData )
	{
		if ( eventData.Speed > 500.0f )
		{
			Discharge();
		}
	}

	[GameEvent.Client.Frame]
	public void HandleWeaponAimingPos()
	{
		if ( !ViewModelEntity.IsValid() ) return;

		var ads = Input.Down( "attack2" );


		ViewModelOffset = ViewModelOffset.LerpTo( ads ? AimPos : Vector3.Zero, Time.Delta * 10f );

		var vm = ViewModelEntity as ViewModel;
		vm.Position = vm.Position + vm.Rotation.Forward * ViewModelOffset.x + vm.Rotation.Right * ViewModelOffset.y + vm.Rotation.Up * ViewModelOffset.z;

		vm.SwingInfluence = vm.SwingInfluence.LerpTo( ads ? 0.0025f : 0.05f, Time.Delta * 2f );
		vm.BobInfluence = vm.BobInfluence.LerpTo( ads ? 0.1f : 1f, Time.Delta * 2f );
	}
}
