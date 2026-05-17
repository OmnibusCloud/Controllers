using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Adapters;
using OutWit.Controller.Matrices.Collections;
using OutWit.Controller.Matrices.Properties;
using OutWit.Controller.Matrices.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices
{
    [WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
    public class WitControllerMatricesModule : WitPluginBase, IWitControllerNode, IWitControllerHost
    {
        public override void Initialize(IServiceCollection services)
        {
            services.AddVariable<WitVariableMatrix>();
            services.AddVariable<WitVariableMatrixSparse>();
            services.AddVariable<WitVariableVector>();
            services.AddVariable<WitVariableVectorSparse>();
            
            services.AddCollection<WitVariableVectorCollection>();
            services.AddCollection<WitVariableVectorSparseCollection>();

            services.AddActivityAdapter<WitActivityMatrixColumnCount, WitActivityAdapterMatrixColumnCount>();
            services.AddActivityAdapter<WitActivityMatrixRowCount, WitActivityAdapterMatrixRowCount>();
            services.AddActivityAdapter<WitActivityMatrixGetRow, WitActivityAdapterMatrixGetRow>();
            services.AddActivityAdapter<WitActivityMatrixGetRows, WitActivityAdapterMatrixGetRows>();
            services.AddActivityAdapter<WitActivityMatrixGetColumn, WitActivityAdapterMatrixGetColumn>();
            services.AddActivityAdapter<WitActivityMatrixGetColumns, WitActivityAdapterMatrixGetColumns>();
            
            services.AddActivityAdapter<WitActivityMatrix, WitActivityAdapterMatrix>();
            services.AddActivityAdapter<WitActivityMatrixSparse, WitActivityAdapterMatrixSparse>();
            services.AddActivityAdapter<WitActivityVector, WitActivityAdapterVector>();
            services.AddActivityAdapter<WitActivityVectorSparse, WitActivityAdapterVectorSparse>();
            
            services.AddActivityAdapter<WitActivityVectorCollection, WitActivityAdapterVectorCollection>();
            services.AddActivityAdapter<WitActivityVectorSparseCollection, WitActivityAdapterVectorSparseCollection>();
            
            services.AddActivityAdapter<WitActivityMatrixGustavsonMultiply, WitActivityAdapterMatrixGustavsonMultiply>();

            services.AddResources<Resources>();
        }
    }
}
