namespace TiaFds.Step7Classic
{
    public static class Step7DiagnosticCodes
    {
        public const string BlockListReadFailed = "S7C001_BLOCK_LIST_READ_FAILED";
        public const string DataBlockDecodeFailed = "S7C002_DB_STRUCTURE_DECODE_FAILED";
        public const string BlockKnowHowProtected = "S7C003_BLOCK_KNOW_HOW_PROTECTED";
        public const string UserDataTypeSymbolNotFound = "S7C004_UDT_SYMBOL_NOT_FOUND";
        public const string SymbolTableNotFound = "S7C005_SYMBOL_TABLE_NOT_FOUND";
        public const string PointerNotResolved = "S7C006_POINTER_NOT_RESOLVED";
        public const string CalledBlockNotInProject = "S7C007_CALLED_BLOCK_NOT_IN_PROJECT";
        public const string SetupRoutineNotRecognised = "S7C008_SETUP_ROUTINE_NOT_RECOGNISED";
        public const string SetupEntryNameMismatch = "S7C009_SETUP_ENTRY_NAME_MISMATCH";
        public const string DriveNotInitialisedBySetup = "S7C010_DRIVE_NOT_INITIALISED_BY_SETUP";
    }
}
