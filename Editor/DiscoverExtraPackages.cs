using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PackageManagerExtraSettings
{
    public static class TypeExtensions
    {
        public static Type GetTypeFromAssembly(string typeName)
        {
            var types = typeof(PackageManagerExtensions).Assembly.GetTypes();
            foreach (var type in types)
            {
                if (type.FullName.Contains(typeName))
                {
                    return type;
                }
            }

            return null;
        }

        public static PropertyInfo GetPropertyFromType(Type type, string propName)
        {
            foreach (var property in type.GetProperties())
            {
                if (property.Name.Contains(propName))
                    return property;
            }

            return null;
        }
        
        public static MethodInfo GetMethodFromType(Type type, string methodName)
        {
            foreach (var method in type.GetMethods())
            {
                if (method.Name.Contains(methodName))
                    return method;
            }
            return null;
        }
    }
    
    public static class DiscoverExtraPackages
    {
        static string[] m_extraPackages = new string[]
        {
            "com.ptc.vuforia.engine",
            "com.unity.2d.entities",
            "com.unity.ai.planner",
            "com.unity.aovrecorder",
            "com.unity.assetbundlebrowser",
            "com.unity.assetgraph",
            "com.unity.barracuda",
            "com.unity.barracuda.burst",
            "com.unity.build-report-inspector",
            "com.unity.cloud.userreporting",
            "com.unity.collections",
            "com.unity.multiplayer-hlapi",
            "com.unity.connect.share",
            "com.unity.dots.editor",
            "com.unity.entities",
            "com.unity.film-tv.toolbox",
            "com.unity.google.resonance.audio",
            "com.unity.immediate-window",
            "com.unity.mathematics",
            "com.unity.meshsync",
            "com.unity.package-manager-doctools",
            "com.unity.package-manager-ui",
            "com.unity.package-validation-suite",
            "com.unity.physics",
            "com.unity.platforms",
            "com.unity.platforms.android",
            "com.unity.platforms.linux",
            "com.unity.platforms.macos",
            "com.unity.platforms.web",
            "com.unity.platforms.windows",
            "com.unity.playablegraph-visualizer",
            "com.unity.render-pipelines.lightweight",
            "com.unity.rendering.hybrid",
            "com.unity.renderstreaming",
            "com.unity.scene-template",
            "com.unity.simulation.client",
            "com.unity.simulation.core",
            "com.unity.simulation.capture",
            "com.unity.simulation.games",
            "com.unity.standardevents",
            "com.unity.streaming-image-sequence",
            "com.unity.test-framework.performance",
            "com.unity.tiny.all",
            "com.unity.transport",
            "com.unity.upm.develop",
            "com.unity.vectorgraphics",
            "com.unity.webrtc",
            "com.unity.xr.googlevr.android",
            "com.unity.xr.googlevr.ios",
            "com.unity.xr.legacyinputhelpers",
            "com.unity.xr.oculus.android",
            "com.unity.xr.oculus.standalone",
            "com.unity.xr.openvr.standalone",
            "com.unity.xr.arsubsystems",
            "com.unity.xr.interactionsubsystems",
            "com.unity.xr.windowsmr.metro",
        };


       
        
        
         static readonly Type m_cacheType = TypeExtensions.GetTypeFromAssembly(".UpmCache");
        static readonly Type m_cacheInterfaceType =  TypeExtensions.GetTypeFromAssembly(".IUpmCache");
        static readonly PropertyInfo m_getPackageInfos =  TypeExtensions.GetPropertyFromType(m_cacheType,"searchPackageInfos") ??  TypeExtensions.GetPropertyFromType(m_cacheInterfaceType,"searchPackageInfos");
        static readonly MethodInfo m_setPackageInfos =  TypeExtensions.GetMethodFromType(m_cacheType,"SetSearchPackageInfos") ??  TypeExtensions.GetMethodFromType(m_cacheInterfaceType,"SetSearchPackageInfos");
        static readonly PropertyInfo m_operationInProgress =  TypeExtensions.GetPropertyFromType( TypeExtensions.GetTypeFromAssembly(".IOperation"), "isInProgress");
        static readonly Type serviceContainerType =  TypeExtensions.GetTypeFromAssembly(".ServicesContainer");
        static readonly Func<object> m_serviceContainerGetter = (Func<object>)serviceContainerType?.BaseType.GetProperty("instance", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).GetGetMethod().CreateDelegate(typeof(Func<object>));
        static readonly Func<object> m_upmCacheGetter = CreateUpmCacheGetter();
        
        static readonly EditorApplication.CallbackFunction m_callback = OnProgress;
        static readonly List<SearchRequest> m_requests = new List<SearchRequest>(32);
        
        static object m_searchAllOperation;
        static Delegate m_eventHandler;

        static Func<object> CreateUpmCacheGetter()
        {
#if UNITY_2020_2_OR_NEWER
        return (Func<object>)serviceContainerType.GetMethod("Resolve").MakeGenericMethod(m_cacheType).CreateDelegate(typeof(Func<object>), m_serviceContainerGetter());
#else
            return (Func<object>)m_cacheType.GetProperty("instance").GetGetMethod().CreateDelegate(typeof(Func<object>));
#endif
        }

        public static void EnableHook(bool enable)
        {

            // Hook package manager SearchAll operation
            var clientType = TypeExtensions.GetTypeFromAssembly(".UpmClient");
#if UNITY_2020_2_OR_NEWER
            var instance = serviceContainerType.GetMethod("Resolve").MakeGenericMethod(clientType).Invoke(m_serviceContainerGetter(), null);
#else
            var instance = clientType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as Object;
#endif
            var evnt = instance.GetType().GetEvent("onSearchAllOperation");
            bool handlerExists = m_eventHandler != null;
            if (!handlerExists)
            {
                m_eventHandler = Delegate.CreateDelegate(evnt.EventHandlerType, new Action<object>(OnSearchAll).Method);
            }
            if (enable)
            {
                if (handlerExists)
                {
                    evnt.RemoveEventHandler(instance, m_eventHandler);
                }
                evnt.AddEventHandler(instance, m_eventHandler);
            }
            else
            {
                evnt.RemoveEventHandler(instance, m_eventHandler);
            }
        }
        
        static void OnSearchAll(object operation)
        {
            if (ExtraSettingsProvider.ShowHiddenPackages)
            {
                m_searchAllOperation = operation;
        
                // Clear requests, in case any searches are running, they will finish without doing anything
                m_requests.Clear();
        
                // Kick off searching
                foreach (var id in m_extraPackages)
                {
                    m_requests.Add(Client.Search(id));
                }
        
                // Re-register update callback
                EditorApplication.update -= m_callback;
                EditorApplication.update += m_callback;
            }
        }
        
        private static void OnProgress()
        {
            // Wait for all requests to complete and for SearchAll opeartion to finish
            if (m_requests.All(s => s.IsCompleted) && !(bool)m_operationInProgress.GetValue(m_searchAllOperation))
            {
                // Then add results
                var cache = m_upmCacheGetter();
                var packages = ((IEnumerable<PackageInfo>)m_getPackageInfos.GetValue(cache)).ToDictionary(p => p.name);
        
                var findedHideProjects = m_requests.Where(s => s.Status == StatusCode.Success).SelectMany(s => s.Result);
        
                foreach (var hideProject in findedHideProjects)
                {
                    if (!packages.ContainsKey(hideProject.name))
                    {
                        packages.Add(hideProject.name, hideProject);
                    }
                }
        
                m_setPackageInfos.Invoke(cache, new object[] { packages.Values });
        
                EditorApplication.update -= m_callback;
                m_requests.Clear();
                m_searchAllOperation = null;
            }
        }
    }
}