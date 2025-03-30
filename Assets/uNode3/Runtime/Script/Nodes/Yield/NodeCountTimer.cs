using UnityEngine;
using System;

namespace MaxyGames.UNode.Nodes {
	[NodeMenu("Yield", "CountTimer", IsCoroutine = true, scope = NodeScope.StateGraph, icon = typeof(TypeIcons.ClockIcon))]
	public class NodeCountTimer : Node {
		[NonSerialized]
		public FlowInput start;
		[NonSerialized]
		public FlowInput stop;
		[NonSerialized]
		public FlowInput reset;

		[NonSerialized]
		public ValueInput count;
		[NonSerialized]
		public ValueInput interval;
		[NonSerialized]
		public ValueInput immediateStart;
		[NonSerialized]
		public ValueInput unscaledTime;

		[NonSerialized]
		public FlowOutput onStarted;
		[NonSerialized]
		public FlowOutput onTick;
		[NonSerialized]
		public FlowOutput onFinished;

		[NonSerialized]
		public ValueOutput currentIndex;
		[NonSerialized]
		public ValueOutput elapsed;
		[NonSerialized]
		public ValueOutput remaining;
		[NonSerialized]
		public ValueOutput isActive;

		class RuntimeData {
			public int count;
			public int currentIndex;
			public float elapsed;
			public float interval;
			public bool unscaled;
			public bool active;
			public bool paused;

			public Action updateAction;

			public bool IsActive => count < 0 || currentIndex < count;
		}

		protected override void OnRegister() {
			onStarted = FlowOutput(nameof(onStarted));
			onTick = FlowOutput(nameof(onTick));
			onFinished = FlowOutput(nameof(onFinished));

			count = ValueInput<int>(nameof(count), -1);
			interval = ValueInput<float>(nameof(interval), 1f);
			immediateStart = ValueInput<bool>(nameof(immediateStart), true);
			unscaledTime = ValueInput<bool>(nameof(unscaledTime), false);

			start = FlowInput(nameof(start), (flow) => {
				var data = flow.GetOrCreateElementData<RuntimeData>(this);
				data.count = count.GetValue<int>(flow);
				data.interval = interval.GetValue<float>(flow);
				data.currentIndex = 0;
				data.elapsed = 0;
				data.unscaled = unscaledTime.GetValue<bool>(flow);
				data.active = true;
				data.paused = false;

				if(immediateStart.GetValue<bool>(flow)) {
					data.elapsed = data.interval;
				}

				if(data.updateAction == null) {
					data.updateAction = () => DoUpdate(flow, data);
					UEvent.Register(UEventID.Update, flow.target as Component, data.updateAction);
				}

				if(onStarted.isAssigned) {
					flow.Next(onStarted);
				}
			});

			stop = FlowInput(nameof(stop), (flow) => {
				var data = flow.GetOrCreateElementData<RuntimeData>(this);
				data.active = false;
				
				if(onFinished.isAssigned) {
					flow.Next(onFinished);
				}
				
				if(data.updateAction != null) {
					UEvent.Unregister(UEventID.Update, flow.target as Component, data.updateAction);
					data.updateAction = null;
				}
			});

			reset = FlowInput(nameof(reset), (flow) => {
				var data = flow.GetOrCreateElementData<RuntimeData>(this);
				data.currentIndex = 0;
				data.elapsed = 0;
			});

			currentIndex = ValueOutput(nameof(currentIndex), typeof(int));
			currentIndex.AssignGetCallback(instance => {
				var data = instance.GetOrCreateElementData<RuntimeData>(this);
				return data.currentIndex;
			});

			elapsed = ValueOutput(nameof(elapsed), typeof(float));
			elapsed.AssignGetCallback(instance => {
				var data = instance.GetOrCreateElementData<RuntimeData>(this);
				return data.elapsed;
			});

			remaining = ValueOutput(nameof(remaining), typeof(float));
			remaining.AssignGetCallback(instance => {
				var data = instance.GetOrCreateElementData<RuntimeData>(this);
				return Mathf.Max(0, data.interval - data.elapsed);
			});

			isActive = ValueOutput(nameof(isActive), typeof(bool));
			isActive.AssignGetCallback(instance => {
				var data = instance.GetOrCreateElementData<RuntimeData>(this);
				return data.IsActive && data.active;
			});
		}

		void DoUpdate(Flow flow, RuntimeData data) {
			if(!data.active || data.paused) return;

			if(!data.IsActive) {
				data.active = false;
				if(onFinished.isAssigned) {
					flow.TriggerParallel(onFinished);
				}
				if(data.updateAction != null) {
					UEvent.Unregister(UEventID.Update, flow.target as Component, data.updateAction);
					data.updateAction = null;
				}
				return;
			}

			data.elapsed += data.unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
			
			if(data.elapsed >= data.interval) {
				data.elapsed = 0;
				data.currentIndex++;
				
				if(onTick.isAssigned) {
					flow.TriggerParallel(onTick);
				}
				
				if(!data.IsActive) {
					data.active = false;
					if(onFinished.isAssigned) {
						flow.TriggerParallel(onFinished);
					}
					if(data.updateAction != null) {
						UEvent.Unregister(UEventID.Update, flow.target as Component, data.updateAction);
						data.updateAction = null;
					}
				}
			}
		}

		public override void OnGeneratorInitialize() {
			var active = CG.RegisterPrivateVariable("timerActive", typeof(bool), false);
			var isPaused = CG.RegisterPrivateVariable("timerPaused", typeof(bool), false);
			var timerCount = CG.RegisterPrivateVariable("timerCount", typeof(int), 0);
			var timerCurrentIndex = CG.RegisterPrivateVariable("timerCurrentIndex", typeof(int), 0);
			var timerElapsed = CG.RegisterPrivateVariable("timerElapsed", typeof(float), 0);
			var timerInterval = CG.RegisterPrivateVariable("timerInterval", typeof(float), 0);
			var timerUnscaled = CG.RegisterPrivateVariable("timerUnscaled", typeof(bool), false);

			CG.RegisterPort(start, () => {
				return CG.Flow(
					timerCount.CGSet(count.CGValue()),
					timerInterval.CGSet(interval.CGValue()),
					timerCurrentIndex.CGSet(0.CGValue()),
					// 修复：使用条件表达式而不是字符串转换
					timerElapsed.CGSet(CG.If(immediateStart.CGValue(), timerInterval, 0.CGValue())),
					timerUnscaled.CGSet(unscaledTime.CGValue()),
					active.CGSet(true.CGValue()),
					isPaused.CGSet(false.CGValue()),
					onStarted.CGFlow(false)
				);
			});

			CG.RegisterPort(stop, () => {
				return CG.Flow(
					active.CGSet(false.CGValue()),
					onFinished.CGFlow(false)
				);
			});

			CG.RegisterPort(reset, () => {
				return CG.Flow(
					timerCurrentIndex.CGSet(0.CGValue()),
					timerElapsed.CGSet(0.CGValue())
				);
			});

			CG.RegisterPort(currentIndex, () => {
				return timerCurrentIndex;
			});

			CG.RegisterPort(elapsed, () => {
				return timerElapsed;
			});

			CG.RegisterPort(remaining, () => {
				return CG.Invoke(typeof(Mathf), nameof(Mathf.Max), CG.Value(0), CG.Subtract(timerInterval, timerElapsed));
			});

			CG.RegisterPort(isActive, () => {
				// 修复：isActive 是变量引用，不是字符串
				return CG.And(
					active.CGValue(),  // 使用 CGValue() 获取变量的值
					CG.Or(
						CG.Compare(timerCount, 0.CGValue(), ComparisonType.LessThan),
						CG.Compare(timerCurrentIndex, timerCount, ComparisonType.LessThan)
					)
				);
			});

			CG.RegisterNodeSetup(this, () => {
				var updateContents = CG.If(
					CG.And(
						active.CGValue(),  // 修复：使用 CGValue() 获取变量的值
						isPaused.CGNot()),
					CG.Flow(
						CG.If(
							CG.Or(
								// 修复：ComparisonType 参数位置错误
								CG.Compare(timerCount, 0.CGValue(), ComparisonType.LessThan),
								CG.Compare(timerCurrentIndex, timerCount, ComparisonType.LessThan)
							),
							CG.Flow(
								// 修复：使用正确的条件表达式
								timerElapsed.CGSet(
									CG.Condition(
										timerUnscaled,
										typeof(Time).CGAccess(nameof(Time.unscaledDeltaTime)),
										typeof(Time).CGAccess(nameof(Time.deltaTime))
									),
									SetType.Add
								),
								CG.If(
									// 修复：ComparisonType 参数位置错误
									CG.Compare(timerElapsed, timerInterval, ComparisonType.GreaterThanOrEqual),
									CG.Flow(
										timerElapsed.CGSet(0.CGValue()),
										timerCurrentIndex.CGSet(1.CGValue(), SetType.Add),
										onTick.CGFlow(false),
										CG.If(
											CG.And(
												// 修复：ComparisonType 参数位置错误
												CG.Compare(timerCount, 0.CGValue(), ComparisonType.GreaterThanOrEqual),
												CG.Compare(timerCurrentIndex, timerCount, ComparisonType.GreaterThanOrEqual)
											),
											CG.Flow(
												active.CGSet(false.CGValue()),
												onFinished.CGFlow(false)
											)
										)
									)
								)
							),
							CG.Flow(
								active.CGSet(false.CGValue()),
								onFinished.CGFlow(false)
							)
						)
					)
				);

				if(CG.includeGraphInformation) {
					updateContents = CG.WrapWithInformation(updateContents, this);
				}
				CG.InsertCodeToFunction("Update", typeof(void), updateContents);
			});
		}
	}
}