﻿using Sandbox;
using Sandbox.Physics;
using System;
using System.Linq;

[Library( "gravgun" )]
public partial class GravGun : Carriable
{
	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	private PhysicsBody holdBody;
	private FixedJoint holdJoint;

	public PhysicsBody HeldBody { get; private set; }
	public Rotation HeldRot { get; private set; }
	public ModelEntity HeldEntity { get; private set; }

	protected virtual float MaxPullDistance => 2000.0f;
	protected virtual float MaxPushDistance => 500.0f;
	protected virtual float LinearFrequency => 10.0f;
	protected virtual float LinearDampingRatio => 1.0f;
	protected virtual float AngularFrequency => 10.0f;
	protected virtual float AngularDampingRatio => 1.0f;
	protected virtual float PullForce => 20.0f;
	protected virtual float PushForce => 1000.0f;
	protected virtual float ThrowForce => 2000.0f;
	protected virtual float HoldDistance => 50.0f;
	protected virtual float AttachDistance => 150.0f;
	protected virtual float DropCooldown => 0.5f;
	protected virtual float BreakLinearForce => 2000.0f;

	private TimeSince timeSinceDrop;

	public override void Spawn()
	{
		base.Spawn();

		Tags.Add( "weapon" );
		SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );
	}

	public override void Simulate( IClient client )
	{
		if ( Owner is not Player owner ) return;

		if ( !Game.IsServer )
			return;

		using ( Prediction.Off() )
		{
			var eyePos = owner.EyePosition;
			var eyeRot = owner.EyeRotation;
			var eyeDir = owner.EyeRotation.Forward;

			if ( HeldBody.IsValid() && HeldBody.PhysicsGroup != null )
			{
				if ( holdJoint.IsValid() && !holdJoint.IsActive )
				{
					GrabEnd();
				}
				else if ( Input.Pressed( InputButton.PrimaryAttack ) )
				{
					if ( HeldBody.PhysicsGroup.BodyCount > 1 )
					{
						// Don't throw ragdolls as hard
						HeldBody.PhysicsGroup.ApplyImpulse( eyeDir * (ThrowForce * 0.5f), true );
						HeldBody.PhysicsGroup.ApplyAngularImpulse( Vector3.Random * ThrowForce, true );
					}
					else
					{
						HeldBody.ApplyImpulse( eyeDir * (HeldBody.Mass * ThrowForce) );
						HeldBody.ApplyAngularImpulse( Vector3.Random * (HeldBody.Mass * ThrowForce) );
					}

					GrabEnd();
				}
				else if ( Input.Pressed( InputButton.SecondaryAttack ) )
				{
					timeSinceDrop = 0;

					GrabEnd();
				}
				else
				{
					GrabMove( eyePos, eyeDir, eyeRot );
				}

				return;
			}

			if ( timeSinceDrop < DropCooldown )
				return;

			var tr = Trace.Ray( eyePos, eyePos + eyeDir * MaxPullDistance )
				.UseHitboxes()
				.WithAnyTags( "solid", "debris", "item" )
				.Ignore( this )
				.Radius( 2.0f )
				.Run();

			if ( !tr.Hit || !tr.Body.IsValid() || !tr.Entity.IsValid() || tr.Entity.IsWorld )
				return;

			if ( tr.Entity.PhysicsGroup == null )
				return;

			var modelEnt = tr.Entity as ModelEntity;
			if ( !modelEnt.IsValid() )
				return;

			var body = tr.Body;

			if ( Input.Pressed( InputButton.PrimaryAttack ) )
			{
				if ( tr.Distance < MaxPushDistance && !IsBodyGrabbed( body ) )
				{
					var pushScale = 1.0f - Math.Clamp( tr.Distance / MaxPushDistance, 0.0f, 1.0f );
					body.ApplyImpulseAt( tr.EndPosition, eyeDir * (body.Mass * (PushForce * pushScale)) );
				}
			}
			else if ( Input.Down( InputButton.SecondaryAttack ) )
			{
				var physicsGroup = tr.Entity.PhysicsGroup;

				if ( physicsGroup.BodyCount > 1 )
				{
					body = modelEnt.PhysicsBody;
					if ( !body.IsValid() )
						return;
				}

				var attachPos = body.FindClosestPoint( eyePos );

				if ( eyePos.Distance( attachPos ) <= AttachDistance )
				{
					var holdDistance = HoldDistance + attachPos.Distance( body.MassCenter );
					GrabStart( modelEnt, body, eyePos + eyeDir * holdDistance, eyeRot );
				}
				else if ( !IsBodyGrabbed( body ) )
				{
					physicsGroup.ApplyImpulse( eyeDir * -PullForce, true );
				}
			}
		}
	}

	private void Activate()
	{
		if ( !holdBody.IsValid() )
		{
			holdBody = new PhysicsBody( Game.PhysicsWorld )
			{
				BodyType = PhysicsBodyType.Keyframed
			};
		}
	}

	private void Deactivate()
	{
		GrabEnd();

		holdBody?.Remove();
		holdBody = null;
	}

	public override void ActiveStart( Entity ent )
	{
		base.ActiveStart( ent );

		if ( Game.IsServer )
		{
			Activate();
		}
	}

	public override void ActiveEnd( Entity ent, bool dropped )
	{
		base.ActiveEnd( ent, dropped );

		if ( Game.IsServer )
		{
			Deactivate();
		}
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		if ( Game.IsServer )
		{
			Deactivate();
		}
	}

	public override void OnCarryDrop( Entity dropper )
	{
	}

	private static bool IsBodyGrabbed( PhysicsBody body )
	{
		// There for sure is a better way to deal with this
		if ( All.OfType<PhysGun>().Any( x => x?.HeldBody?.PhysicsGroup == body?.PhysicsGroup ) ) return true;
		if ( All.OfType<GravGun>().Any( x => x?.HeldBody?.PhysicsGroup == body?.PhysicsGroup ) ) return true;

		return false;
	}

	private void GrabStart( ModelEntity entity, PhysicsBody body, Vector3 grabPos, Rotation grabRot )
	{
		if ( !body.IsValid() )
			return;

		if ( body.PhysicsGroup == null )
			return;

		if ( IsBodyGrabbed( body ) )
			return;

		GrabEnd();

		HeldBody = body;
		HeldRot = grabRot.Inverse * HeldBody.Rotation;

		holdBody.Position = grabPos;
		holdBody.Rotation = HeldBody.Rotation;

		HeldBody.Sleeping = false;
		HeldBody.AutoSleep = false;

		holdJoint = PhysicsJoint.CreateFixed( holdBody, HeldBody.MassCenterPoint() );
		holdJoint.SpringLinear = new( LinearFrequency, LinearDampingRatio );
		holdJoint.SpringAngular = new( AngularFrequency, AngularDampingRatio );
		holdJoint.Strength = HeldBody.Mass * BreakLinearForce;

		HeldEntity = entity;

		Client?.Pvs.Add( HeldEntity );
	}

	private void GrabEnd()
	{
		holdJoint?.Remove();
		holdJoint = null;

		if ( HeldBody.IsValid() )
		{
			HeldBody.AutoSleep = true;
		}

		if ( HeldEntity.IsValid() )
		{
			Client?.Pvs.Remove( HeldEntity );
		}

		HeldBody = null;
		HeldRot = Rotation.Identity;
		HeldEntity = null;
	}

	private void GrabMove( Vector3 startPos, Vector3 dir, Rotation rot )
	{
		if ( !HeldBody.IsValid() )
			return;

		var attachPos = HeldBody.FindClosestPoint( startPos );
		var holdDistance = HoldDistance + attachPos.Distance( HeldBody.MassCenter );

		holdBody.Position = startPos + dir * holdDistance;
		holdBody.Rotation = rot * HeldRot;
	}

	public override bool IsUsable( Entity user )
	{
		return Owner == null || HeldBody.IsValid();
	}
}
