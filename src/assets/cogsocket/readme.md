# In-Sight Web SDK Sample Pages
These pages and associated script provide simple examples for using the In-Sight HMI API

## cogsocket_test.html
This page allows a connection to a designated camera using CogSocket.
The user is able to make GET/PUT/POST requests and add event listeners.

## display_results.html
This page allows a connection to a designated camera using CogSocket.
Upon connection, it opens a session, logs in, and begins to receive results.
A 'ready' is sent after each result is received and displayed.
A 'keepAlive' is sent every 10 seconds to prevent the session from being disposed.
