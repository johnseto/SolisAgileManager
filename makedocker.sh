# First arg is version
if [ -z "$1" ]; then
    echo 'No docker tag specified. Pushing to dev'
    DOCKERTAG='dev'

    echo "**** Building Docker SolisAgileManager using dev base image"
    docker build . -t solisagilemanager
else
    DOCKERTAG="$1"
    echo "Master specified - creating tag: ${DOCKERTAG}"

    echo "**** Building Docker Release for SolisAgileManager"
    docker build . -t solisagilemanager
fi


echo "*** Pushing docker image to webreaper/solisagilemanager:${DOCKERTAG}"

docker tag solisagilemanager webreaper/solisagilemanager:$DOCKERTAG
docker push webreaper/solisagilemanager:$DOCKERTAG

if [ -z "$1" ]; then
    docker push webreaper/solisagilemanager:dev
else
    echo "*** Pushing docker image to webreaper/solisagilemanager:latest"
    docker tag solisagilemanager webreaper/solisagilemanager:latest
    docker push webreaper/solisagilemanager:latest
fi

echo "Solis Agile Manager docker build complete."
