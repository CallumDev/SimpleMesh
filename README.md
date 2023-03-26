# SimpleMesh

SimpleMesh is a C# library for loading mesh geometry into an engine-agnostic format.
 It provides its own simple binary format to save imported meshes into for runtime load performance.

Requires .NET 6.0 SDK to build. The main SimpleMesh library can be built for .NET 5.0 or .NET 6.0

Current supported formats:

* Collada (.dae)
* Wavefront Obj (.obj)
* GLTF 2.0 (.gltf/.glb) - with the restriction all buffers must be embedded in the file.

Things SimpleMesh does not do:

* Textures - This is outside of the scope of the library.

## Coordinate Space

SimpleMesh imports and exports all models as Y Up


Texture Coordinates: First image pixel (UV coordinates origin) corresponds to the upper left corner of the image. 
Implementation Note: OpenGL-based implementations must flip Y axis to achieve correct texture sampling.

See **SampleOpenTK** to see the library in use.
