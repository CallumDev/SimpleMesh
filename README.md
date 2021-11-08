# SimpleMesh

SimpleMesh is a C# library for loading mesh geometry into an engine-agnostic format.
 It provides its own simple binary format to save imported meshes into for runtime load performance.

Requires .NET 5.0 SDK to build.

Current supported formats:

* Collada (.dae)
* Wavefront Obj (.obj)
* GLTF 2.0 (.gltf/.glb) - with the restriction all buffers must be embedded in the file.

Things SimpleMesh does not do:

* Textures - This is outside of the scope of the library.


See **SampleOpenTK** to see the library in use.
