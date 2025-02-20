﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos
{
    using System.Runtime.Serialization;

    /// <summary>
    /// Defines the target data type of a vector index specification in the Azure Cosmos DB service.
    /// </summary>
    internal enum VectorDataType
    {
        /// <summary>
        /// Represent a float16 data type.
        /// </summary>
        [EnumMember(Value = "float16")]
        Float16,

        /// <summary>
        /// Represent a float32 data type.
        /// </summary>
        [EnumMember(Value = "float32")]
        Float32,

        /// <summary>
        /// Represent an uint8 data type.
        /// </summary>
        [EnumMember(Value = "uint8")]
        Uint8,

        /// <summary>
        /// Represent a int8 data type.
        /// </summary>
        [EnumMember(Value = "int8")]
        Int8
    }
}
