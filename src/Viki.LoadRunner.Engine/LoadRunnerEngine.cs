﻿using System;
using System.Diagnostics;
using System.Threading;
using Viki.LoadRunner.Engine.Aggregators;
using Viki.LoadRunner.Engine.Executor;
using Viki.LoadRunner.Engine.Executor.Context;
using Viki.LoadRunner.Engine.Executor.Threads;

namespace Viki.LoadRunner.Engine
{
    public class LoadRunnerEngine
    {
        #region Fields

        private readonly ExecutionParameters _parameters;
        private readonly IResultsAggregator _resultsAggregator;
        private readonly Type _iTestScenarioObjeType;

        #endregion

        #region Ctor

        public LoadRunnerEngine(ExecutionParameters parameters, Type iTestScenarioObjectType, params IResultsAggregator[] resultsAggregators)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (iTestScenarioObjectType == null)
                throw new ArgumentNullException(nameof(iTestScenarioObjectType));

            _parameters = parameters;
            _iTestScenarioObjeType = iTestScenarioObjectType;

            _resultsAggregator = new AsyncResultsAggregator(resultsAggregators);

        }

        public static LoadRunnerEngine Create<TTestScenario>(ExecutionParameters parameters, params IResultsAggregator[] resultsAggregators) where TTestScenario : ILoadTestScenario
        {
            return new LoadRunnerEngine(parameters, typeof(TTestScenario), resultsAggregators);
        }

        #endregion

        #region Methods

        public void Run()
        {
            ThreadCoordinator threadCoordinator = null;
            try
            {
                threadCoordinator = new ThreadCoordinator(_parameters.MinThreads, _parameters.MaxThreads, _iTestScenarioObjeType);
                threadCoordinator.ScenarioExecutionFinished += _threadCoordinator_ScenarioExecutionFinished;

                _resultsAggregator.Begin();

                TimeSpan minimumDelayBetweenTests = TimeSpan.FromTicks((int)((TimeSpan.FromSeconds(1).Ticks / _parameters.MaxRequestsPerSecond) + 0.5));
                int testIterationCount = 0;
                TimeSpan lastExecutionQueued = TimeSpan.Zero;
                TimeSpan elapsedTime = TimeSpan.Zero - minimumDelayBetweenTests;

                DateTime testBeginTime = DateTime.UtcNow;
                while (elapsedTime <= _parameters.MaxDuration && testIterationCount < _parameters.MaxIterationsCount)
                {
                    threadCoordinator.AssertThreadErrors();

                    if (threadCoordinator.AvailableThreadCount == 0)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    if (elapsedTime - lastExecutionQueued >= minimumDelayBetweenTests && threadCoordinator.AvailableThreadCount > 0)
                    {
                        if (threadCoordinator.TryRunSingleIteration())
                        {
                            lastExecutionQueued = elapsedTime;
                            testIterationCount++;
                        }
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }

                    elapsedTime = DateTime.UtcNow - testBeginTime;
                }
                
            }
            finally
            {
                threadCoordinator?.StopAndDispose(_parameters.FinishTimeoutMilliseconds);
                _resultsAggregator.End();

                threadCoordinator?.AssertThreadErrors();
            }
        }

        #endregion

        #region Events

        private void _threadCoordinator_ScenarioExecutionFinished(object sender, TestContextResult result)
        {
            _resultsAggregator.TestContextResultReceived(result);
        }

        #endregion
    }
}