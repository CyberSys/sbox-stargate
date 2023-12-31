﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;

public partial class EventHorizonTrigger : ModelEntity
{
	private EventHorizon EventHorizon = null;
	public string TriggerModel = "models/sbox_stargate/event_horizon/event_horizon_trigger.vmdl";

	public EventHorizonTrigger()
	{
	}

	public EventHorizonTrigger(EventHorizon eh)
	{
		EventHorizon = eh;
	}

	public EventHorizonTrigger( EventHorizon eh, string model )
	{
		EventHorizon = eh;
		TriggerModel = model;

		Spawn();
	}

	public override void Spawn()
	{
		base.Spawn();

		Transmit = TransmitType.Always;

		SetModel( TriggerModel );
		SetupPhysicsFromModel( PhysicsMotionType.Static, true );

		Tags.Add( "trigger" );

		EnableAllCollisions = false;
		EnableTraceAndQueries = false;
		EnableTouch = true;
	}

	public override void StartTouch( Entity other )
	{
		base.StartTouch( other );

		if ( !Game.IsServer ) return;

		EventHorizon.OnEntityTriggerStartTouch( this, other );
	}

	public override void EndTouch( Entity other )
	{
		base.EndTouch( other );

		if ( !Game.IsServer ) return;

		EventHorizon.OnEntityTriggerEndTouch( this, other );
	}
}
