using UnityEngine;

public enum ContextActionKind { Primary, Secondary }

public interface IContextActionConsumer
{
    bool TryConsumeContextAction(ContextActionKind action, Transform interactor);
}
