# Use the official lightweight Node.js image
FROM node:20-slim

# Create and set the working directory
WORKDIR /usr/src/app

# Copy package files and install only production dependencies
COPY package*.json ./
RUN npm ci --only=production

# Copy the rest of the server code
COPY server.js .

# Expose the port Render uses
EXPOSE 10000

# Start the server
CMD [ "node", "server.js" ]
