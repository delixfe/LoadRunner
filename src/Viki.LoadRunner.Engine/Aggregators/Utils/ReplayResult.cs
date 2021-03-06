﻿using System;
using System.Diagnostics;
using System.Linq;
using Viki.LoadRunner.Engine.Executor.Context;
using Viki.LoadRunner.Engine.Executor.Result;

namespace Viki.LoadRunner.Engine.Aggregators.Utils
{
    [DebuggerDisplay("T:{ThreadIterationId} G:{GlobalIterationId} L:{ThreadIterationId} TS:{(int)(IterationStarted.TotalMilliseconds)}")]
    public class ReplayResult<TUserData> : IterationResult
    {
        public new Checkpoint[] Checkpoints
        {
            get { return (Checkpoint[]) base.Checkpoints; }
            set { base.Checkpoints = value.Cast<ICheckpoint>().ToArray();  }
        }

        public new TUserData UserData
        {
            get { return (TUserData)((IIterationMetadata<object>)this).UserData; }
            set { ((IIterationMetadata<object>)this).UserData = value; }
        }

        public void Offset(TimeSpan offset)
        {
            IterationStarted += offset;
            IterationFinished += offset;
        }
    }
}