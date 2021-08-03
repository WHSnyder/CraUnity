using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Jobs;


public interface ICraAnimated
{
    CraAnimator GetAnimator();
}
public class CraAnimatorManager
{
    public static CraAnimatorManager Instance { get; private set; }

    struct CraLayerData
    {
        public CraPlayer[] States;
        public int CurrentStateIdx;
        public Action OnTransitFinished;
        public Action OnStateFinished;
        public bool OnStateFinishedInvoked;
    }

    struct CraAnimatorData
    {
        public CraLayer[] Layers;
        public Action<int> OnTransitFinished;
        public Action<int> OnStateFinished;
    }

    CraDataContainerManaged<CraLayerData>    Layers;
    CraDataContainerManaged<CraAnimatorData> Animators;

    CraAnimatorManager()
    {
        Layers    = new CraDataContainerManaged<CraLayerData>(CraSettings.MAX_LAYERS);
        Animators = new CraDataContainerManaged<CraAnimatorData>(CraSettings.MAX_ANIMATORS);
    }

    public static CraAnimatorManager Get()
    {
        if (Instance != null)
        {
            return Instance;
        }

        Instance = new CraAnimatorManager();
        return Instance;
    }


    public CraHandle LayerNew(int maxStates)
    {
        int idx = Layers.Alloc();
        if (idx < 0)
        {
            return new CraHandle(-1);
        }

        CraLayerData data = Layers.Get(idx);
        data.States = new CraPlayer[maxStates];
        data.OnStateFinished = null;
        data.OnTransitFinished= null;
        data.CurrentStateIdx = CraSettings.STATE_NONE;
        for (int i = 0; i < data.States.Length; ++i)
        {
            data.States[i] = CraPlayer.CreateEmpty();
        }

        Layers.Set(idx, in data);
        return new CraHandle(idx);
    }

    public int LayerAddState(CraHandle layer, CraPlayer state)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        for (int i = 0; i < data.States.Length; ++i)
        {
            if (!data.States[i].IsValid())
            {
                data.States[i] = state;
                Layers.Set(layer.Handle, in data);
                return i;
            }
        }

        Debug.LogError($"Reached max available state slots of {data.States.Length} in layer at index {layer.Handle}!");
        return CraSettings.STATE_NONE;
    }

    public CraPlayer LayerGetCurrentState(CraHandle layer)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        if (data.CurrentStateIdx != CraSettings.STATE_NONE)
        {
            Debug.Assert(data.States[data.CurrentStateIdx].IsValid());
            return data.States[data.CurrentStateIdx];
        }
        return CraPlayer.CreateEmpty();
    }

    public int LayerGetCurrentStateIdx(CraHandle layer)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        if (data.CurrentStateIdx != CraSettings.STATE_NONE)
        {
            Debug.Assert(data.States[data.CurrentStateIdx].IsValid());
            return data.CurrentStateIdx;
        }
        return -1;
    }

    public void LayerSetState(CraHandle layer, int stateIdx)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        if (stateIdx == data.CurrentStateIdx) return;

        Debug.Assert(stateIdx < data.States.Length);
        if (data.CurrentStateIdx != CraSettings.STATE_NONE)
        {
            data.States[data.CurrentStateIdx].Reset();
        }
        data.CurrentStateIdx = stateIdx;
        if (stateIdx != CraSettings.STATE_NONE)
        {
            data.States[stateIdx].CaptureBones();
            data.States[stateIdx].Play(true);
            data.OnStateFinishedInvoked = false;
        }
        Layers.Set(layer.Handle, in data);
    }

    public void LayerCaptureBones(CraHandle layer)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        if (data.CurrentStateIdx != CraSettings.STATE_NONE)
        {
            Debug.Assert(data.States[data.CurrentStateIdx].IsValid());
            data.States[data.CurrentStateIdx].CaptureBones();
        }
    }

    public void LayerRestartState(CraHandle layer)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        if (data.CurrentStateIdx != CraSettings.STATE_NONE)
        {
            data.States[data.CurrentStateIdx].Play(true);
            data.OnStateFinishedInvoked = false;
        }
        Layers.Set(layer.Handle, in data);
    }

    public void LayerTransitFromAboveLayer(CraHandle layer)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        Debug.Assert(data.CurrentStateIdx != CraSettings.STATE_NONE);
        data.States[data.CurrentStateIdx].ResetTransition();
    }

    public void LayerSetPlaybackSpeed(CraHandle layer, int stateIdx, float playbackSpeed)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        data.States[stateIdx].SetPlaybackSpeed(playbackSpeed);
    }

    public void LayerAddOnTransitFinishedListener(CraHandle layer, Action callback)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        data.OnTransitFinished += callback;
        Layers.Set(layer.Handle, in data);
    }

    public void LayerAddOnStateFinishedListener(CraHandle layer, Action callback)
    {
        CraLayerData data = Layers.Get(layer.Handle);
        data.OnStateFinished += callback;
        Layers.Set(layer.Handle, in data);
    }

    public CraHandle AnimatorNew(int numLayers, int maxStatesPerLayer)
    {
        int idx = Animators.Alloc();
        if (idx < 0)
        {
            return new CraHandle(-1);
        }

        CraAnimatorData data = Animators.Get(idx);
        data.OnStateFinished = null;
        data.OnTransitFinished = null;

        data.Layers = new CraLayer[numLayers];
        for (int i = 0; i < data.Layers.Length; ++i)
        {
            data.Layers[i] = CraLayer.CreateNew(maxStatesPerLayer);
        }

        Animators.Set(idx, in data);
        return new CraHandle(idx);
    }

    public int AnimatorAddState(CraHandle animator, int layer, CraPlayer state)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        Debug.Assert(layer >= 0 && layer < data.Layers.Length);
        Debug.Assert(state.IsValid());

        return data.Layers[layer].AddState(state);
    }

    public CraPlayer AnimatorGetCurrentState(CraHandle animator, int layer)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        Debug.Assert(layer >= 0 && layer < data.Layers.Length);

        return data.Layers[layer].GetCurrentState();
    }

    public int AnimatorGetCurrentStateIdx(CraHandle animator, int layer)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        Debug.Assert(layer >= 0 && layer < data.Layers.Length);
        return data.Layers[layer].GetCurrentStateIdx();
    }

    public void AnimatorSetState(CraHandle animator, int layer, int stateIdx)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        Debug.Assert(layer >= 0 && layer < data.Layers.Length);

        data.Layers[layer].SetState(stateIdx);

        // if some layer in between gets removed (e.g. 1), the next layer
        // below it (e.g. 0) should do a transition, since it now has
        // potentially authority of more bones again
        if (stateIdx == CraSettings.STATE_NONE)
        {
            for (int i = layer - 1; i >= 0; --i)
            {
                if (data.Layers[i].GetCurrentStateIdx() != CraSettings.STATE_NONE)
                {
                    data.Layers[i].TransitFromAboveLayer();
                    break;
                }
            }
        }

        // rebuild bone authority
        for (int i = 0; i < data.Layers.Length; ++i)
        {
            data.Layers[i].CaptureBones();
        }
    }

    public void AnimatorSetPlaybackSpeed(CraHandle animator, int layer, int stateIdx, float playbackSpeed)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        Debug.Assert(layer >= 0 && layer < data.Layers.Length);

        data.Layers[layer].SetPlaybackSpeed(stateIdx, playbackSpeed);
    }

    public void AnimatorRestartState(CraHandle animator, int layer)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        Debug.Assert(layer >= 0 && layer < data.Layers.Length);

        data.Layers[layer].RestartState();
    }

    public void AnimatorAddOnTransitFinishedListener(CraHandle animator, Action<int> callback)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        data.OnStateFinished += callback;
        Animators.Set(animator.Handle, in data);
    }

    public void AnimatorAddOnStateFinishedListener(CraHandle animator, Action<int> callback)
    {
        CraAnimatorData data = Animators.Get(animator.Handle);
        data.OnStateFinished += callback;
        Animators.Set(animator.Handle, in data);
    }


    public void Clear()
    {
        Layers.Clear();
        Animators.Clear();
    }

    public void Destroy()
    {
        Layers.Destroy();
        Animators.Destroy();
    }

    public void Tick()
    {
        // Invoke layer events on main thread!
        for (int i = 0; i < Layers.GetNumAllocated(); ++i)
        {
            CraLayerData data = Layers.Get(i);

            if (data.CurrentStateIdx > -1)
            {
                ref CraPlayer state = ref data.States[data.CurrentStateIdx];
                if (!data.OnStateFinishedInvoked && state.IsFinished())
                {
                    data.OnStateFinished?.Invoke();
                    data.OnStateFinishedInvoked = true;
                }
            }
        }
    }
}
