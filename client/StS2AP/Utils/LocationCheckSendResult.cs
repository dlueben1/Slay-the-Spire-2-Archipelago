namespace StS2AP.Utils
{
    internal readonly record struct LocationCheckSendResult(
        LocationCheckSendResult.DispatchStatus Dispatch,
        int RequestedCount,
        int AcceptedCount,
        int AlreadyCheckedCount,
        int NotInSlotCount
    )
    {
        public bool WasRecorded => AcceptedCount > 0 || AlreadyCheckedCount > 0;

        internal enum DispatchStatus
        {
            None,
            Submitted,
            Queued,
            NoAuthenticatedSlot,
            PersistenceFailed,
        }
    }
}
