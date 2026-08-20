public interface IConcreteBatchReceiver
{
    bool CanReceiveConcreteBatch { get; }
    bool TryReceiveConcreteBatch(ConcreteMixerController source);
}
