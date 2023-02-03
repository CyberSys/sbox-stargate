﻿using Sandbox;
using Sandbox.UI;

using System;
using System.Linq;

public class SGCProgram : Panel
{
	public SGCMonitor Monitor;
	public SGCComputer Computer;

	protected Stargate Gate;

	public void UpdateProgram( SGCMonitor monitor, SGCComputer computer)
	{
		Monitor = monitor;
		Computer = computer;
	}

	public override void Tick()
	{
		base.Tick();

		if ( !Computer.IsValid() )
			return;

		if (Gate != Computer.Gate)
			Gate = Computer.Gate;
	}
}
