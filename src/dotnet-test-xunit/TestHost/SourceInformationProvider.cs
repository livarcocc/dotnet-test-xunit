﻿using Xunit.Abstractions;
using TestHostSourceInformationProvider = Microsoft.Extensions.Testing.Abstractions.ISourceInformationProvider;

namespace Xunit.Runner.DotNet
{
    public class SourceInformationProviderAdapater : LongLivedMarshalByRefObject, ISourceInformationProvider
    {
        readonly TestHostSourceInformationProvider provider;

        public SourceInformationProviderAdapater(TestHostSourceInformationProvider provider)
        {
            this.provider = provider;
        }

        public void Dispose() { }

        public ISourceInformation GetSourceInformation(ITestCase testCase)
        {
            if (provider == null)
                return null;

            var reflectionMethodInfo = testCase.TestMethod.Method as IReflectionMethodInfo;
            if (reflectionMethodInfo == null)
                return null;

            var innerInformation = provider.GetSourceInformation(reflectionMethodInfo.MethodInfo);
            if (innerInformation == null)
                return null;

            return new SourceInformation
            {
                FileName = innerInformation.Filename,
                LineNumber = innerInformation.LineNumber,
            };
        }

        class SourceInformation : ISourceInformation
        {
            public string FileName { get; set; }

            public int? LineNumber { get; set; }

            public void Deserialize(IXunitSerializationInfo info)
            {
                FileName = info.GetValue<string>("FileName");
                LineNumber = info.GetValue<int?>("LineNumber");
            }

            public void Serialize(IXunitSerializationInfo info)
            {
                info.AddValue("FileName", FileName, typeof(string));
                info.AddValue("LineNumber", LineNumber, typeof(int?));
            }
        }
    }
}
