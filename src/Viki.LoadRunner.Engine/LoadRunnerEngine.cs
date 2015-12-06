﻿using System;
using System.Threading;
using Viki.LoadRunner.Engine.Aggregators;
using Viki.LoadRunner.Engine.Executor.Context;
using Viki.LoadRunner.Engine.Executor.Threads;
using Viki.LoadRunner.Engine.Executor.Timer;
using Viki.LoadRunner.Engine.Parameters;

namespace Viki.LoadRunner.Engine
{
    /// <summary>
    /// ILoadTestScenario executor
    /// </summary>
    public class LoadRunnerEngine
    {
        #region Fields

        private readonly LoadRunnerParameters _parameters;
        private readonly IResultsAggregator _resultsAggregator;
        private readonly Type _iTestScenarioObjectType;

        #region Run() globals

        private readonly ExecutionTimer _timer = new ExecutionTimer();
        private ThreadCoordinator _threadCoordinator;

        #endregion

        #endregion

        #region Properties

        /// <summary>
        /// Current duration of currently executing load test
        /// </summary>
        public TimeSpan TestDuration => _timer.CurrentValue;
        /// <summary>
        /// Start UTC time for currently executing load test
        /// </summary>
        public DateTime TestBeginTimeUtc => _timer.BeginTime;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes new executor instance
        /// </summary>
        /// <param name="parameters">LoadTest parameters</param>
        /// <param name="iTestScenarioObjectType">ILoadTestScenario to be executed object type</param>
        /// <param name="resultsAggregators">Aggregators to use when aggregating results from all iterations</param>
        public LoadRunnerEngine(LoadRunnerParameters parameters, Type iTestScenarioObjectType, params IResultsAggregator[] resultsAggregators)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (iTestScenarioObjectType == null)
                throw new ArgumentNullException(nameof(iTestScenarioObjectType));

            _parameters = parameters;
            _iTestScenarioObjectType = iTestScenarioObjectType;

            _resultsAggregator = new AsyncResultsAggregator(resultsAggregators);
        }

        /// <summary>
        /// Initializes new executor instance
        /// </summary>
        /// <typeparam name="TTestScenario">ILoadTestScenario to be executed object type</typeparam>
        /// <param name="parameters">LoadTest parameters</param>
        /// <param name="resultsAggregators">Aggregators to use when aggregating results from all iterations</param>
        /// <returns></returns>
        public static LoadRunnerEngine Create<TTestScenario>(LoadRunnerParameters parameters, params IResultsAggregator[] resultsAggregators) 
            where TTestScenario : ILoadTestScenario
        {
            return new LoadRunnerEngine(parameters, typeof(TTestScenario), resultsAggregators);
        }

        #endregion

        #region Run() Stuff

        /// <summary>
        /// Start LoadTest execution.
        /// This is a blocking call and will finish only, once the test is over and all results are aggregated by IResultsAggregator's
        /// </summary>
        public void Run()
        {
            try
            {
                _threadCoordinator = new ThreadCoordinator(
                    _iTestScenarioObjectType,
                    _timer,
                    _parameters.ThreadingStrategy.InitialThreadCount
                )
                {
                    InitialUserData = _parameters.InitialUserData
                };
                _threadCoordinator.ScenarioIterationFinished += _threadCoordinator_ScenarioIterationFinished;
                
                int testIterationCount = 0;
                TimeSpan executionEnqueueThreshold = TimeSpan.Zero;

                _timer.Start();
                _resultsAggregator.Begin(_timer.BeginTime);
                
                while (_timer.CurrentValue <= _parameters.Limits.MaxDuration && testIterationCount < _parameters.Limits.MaxIterationsCount)
                {
                    WorkerThreadStats threadStats = _threadCoordinator.BuildWorkerThreadStats();
                    int allowedWorkingthreadCount = _parameters.ThreadingStrategy.GetAllowedMaxWorkingThreadCount(_timer.CurrentValue, threadStats);

                    _threadCoordinator.AssertThreadErrors();
                    TryAdjustCreatedThreadCount(threadStats);

                    
                    if (allowedWorkingthreadCount > threadStats.WorkingThreadCount && _timer.CurrentValue >= executionEnqueueThreshold)
                    {
                        if (_threadCoordinator.TryEnqueueSingleIteration())
                        {
                            executionEnqueueThreshold = CalculateNextExecutionTime(executionEnqueueThreshold);
                            testIterationCount++;
                        }
                        else
                            Thread.Sleep(TimeSpan.FromTicks(5000));
                    }
                    else
                    {
                        Thread.Sleep(TimeSpan.FromTicks(5000));
                    }

                    //_timer.UpdateCurrent();
                }
            }
            finally
            {
                _threadCoordinator?.StopAndDispose((int)_parameters.Limits.FinishTimeout.TotalMilliseconds);
                _resultsAggregator.End();
                _threadCoordinator?.AssertThreadErrors();

                _threadCoordinator = null;
                _timer.Stop();
            }
        }

        private void TryAdjustCreatedThreadCount(WorkerThreadStats threadStats)
        {
            int allowedCreatedThreadCount = _parameters.ThreadingStrategy.GetAllowedCreatedThreadCount(_timer.CurrentValue, threadStats);

            if (allowedCreatedThreadCount > threadStats.CreatedThreadCount)
                _threadCoordinator.InitializeThreadsAsync(_parameters.ThreadingStrategy.ThreadCreateBatchSize);
            else if (allowedCreatedThreadCount < threadStats.CreatedThreadCount)
                _threadCoordinator.StopWorkersAsync(threadStats.CreatedThreadCount - allowedCreatedThreadCount);
        }

        private TimeSpan CalculateNextExecutionTime(TimeSpan lastExecutionEnqueueThreshold)
        {
            TimeSpan delayBetweenIterations = _parameters.SpeedStrategy.GetDelayBetweenIterations(_timer.CurrentValue);

            TimeSpan nextExecutionTime = lastExecutionEnqueueThreshold + delayBetweenIterations;
            if (nextExecutionTime < _timer.CurrentValue)
                nextExecutionTime = _timer.CurrentValue;

            return nextExecutionTime;
        }

        #endregion

        #region Events

        private void _threadCoordinator_ScenarioIterationFinished(TestContextResult result)
        {
            _resultsAggregator.TestContextResultReceived(result);
        }

        #endregion
    }
}