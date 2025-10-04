# Docker Container Deployment to ECR

This guide explains how to build and publish Docker containers to the ECR repository for the MagicPets system.

## Prerequisites

- AWS CLI configured with appropriate permissions
- Docker installed and running
- System stack deployed (creates the ECR repository)

## Environment Variables

You'll need these values from your deployment:
- `AWS_PROFILE`: Your AWS SSO profile (e.g., lzm-dev)
- `AWS_ACCOUNT_ID`: Your AWS account ID
- `AWS_REGION`: Your deployment region (e.g., us-west-2)
- `SYSTEM_KEY`: Your system key (e.g., mp)
- `SYSTEM_SUFFIX`: Your system suffix (e.g., dev)

The ECR repository name follows the pattern: `${SYSTEM_KEY}-ecr-${SYSTEM_SUFFIX}`

## Step-by-Step Deployment

### 1. Navigate to Container Directory
```bash
cd /path/to/MagicPets/Service/Containers/ChatAppRunner
```

### 2. Authenticate Docker to ECR
```bash
aws ecr get-login-password --profile ${AWS_PROFILE} --region ${AWS_REGION} | docker login --username AWS --password-stdin ${AWS_ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com
```

### 3. Build the Docker Image
```bash
docker build -t chat-app-runner .
```

### 4. Tag the Image for ECR
```bash
docker tag chat-app-runner:latest ${AWS_ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com/${SYSTEM_KEY}-ecr-${SYSTEM_SUFFIX}:latest
```

### 5. Push the Image to ECR
```bash
docker push ${AWS_ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com/${SYSTEM_KEY}-ecr-${SYSTEM_SUFFIX}:latest
```

## Example with Actual Values

If your deployment uses:
- AWS Profile: `lzm-dev`
- AWS Account ID: `123456789012`
- Region: `us-west-2`
- System Key: `mp`
- System Suffix: `dev`

```bash
# Navigate to container
cd /path/to/MagicPets/Service/Containers/ChatAppRunner

# Authenticate
aws ecr get-login-password --profile lzm-dev --region us-west-2 | docker login --username AWS --password-stdin 123456789012.dkr.ecr.us-west-2.amazonaws.com

# Build
docker build -t chat-app-runner .

# Tag
docker tag chat-app-runner:latest 123456789012.dkr.ecr.us-west-2.amazonaws.com/mp-ecr-dev:latest

# Push
docker push 123456789012.dkr.ecr.us-west-2.amazonaws.com/mp-ecr-dev:latest
```

## Automated Script

You can create a script to automate this process:

```bash
#!/bin/bash

# Set your deployment variables
export AWS_PROFILE="lzm-dev"
export AWS_ACCOUNT_ID="123456789012"
export AWS_REGION="us-west-2"
export SYSTEM_KEY="mp"
export SYSTEM_SUFFIX="dev"

# Build and push
ECR_REPO="${AWS_ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com/${SYSTEM_KEY}-ecr-${SYSTEM_SUFFIX}"

echo "Authenticating to ECR..."
aws ecr get-login-password --profile ${AWS_PROFILE} --region ${AWS_REGION} | docker login --username AWS --password-stdin ${ECR_REPO}

echo "Building Docker image..."
docker build -t chat-app-runner .

echo "Tagging image for ECR..."
docker tag chat-app-runner:latest ${ECR_REPO}:latest

echo "Pushing to ECR..."
docker push ${ECR_REPO}:latest

echo "Deployment complete! Image available at: ${ECR_REPO}:latest"
```

## Verification

After pushing, you can verify the image was uploaded by:

1. **AWS Console**: Navigate to ECR → Repositories → your repository name
2. **AWS CLI**:
   ```bash
   aws ecr describe-images --profile ${AWS_PROFILE} --repository-name ${SYSTEM_KEY}-ecr-${SYSTEM_SUFFIX} --region ${AWS_REGION}
   ```

## Notes

- The ECR repository is created by the system stack deployment
- Images are automatically scanned for vulnerabilities when pushed
- Untagged images are automatically cleaned up after 1 day (configured in lifecycle policy)
- The App Runner service will automatically pull from this ECR repository when deployed