﻿using ITCC.HTTP.Enums;

namespace ITCC.HTTP.Server.Files.Preprocess
{
    internal class VideoPreprocessTask : BaseFilePreprocessTask
    {
        #region BaseFilePreprocessTask
        public override FileType FileType => FileType.Image;
        public override string FileName { get; set; }

        public override bool Perform()
        {
            return true;
        }
        #endregion
    }
}
